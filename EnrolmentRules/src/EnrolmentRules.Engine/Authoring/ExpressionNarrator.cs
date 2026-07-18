namespace EnrolmentRules.Engine.Authoring;

using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using Domain;

/// <summary>
///     Turns a workflow rule expression into student-facing English, resolving every policy member
///     against the <em>loaded</em> <see cref="PolicyThresholds" /> so the wording tracks
///     <c>thresholds.yaml</c> rather than restating it.
///     <para>
///         This exists because the alternative — a hand-written caption beside each rule — would drift
///         from the expression it describes with nothing to catch it, which is precisely Reservation 1
///         (rules are untyped; a transposed comparison compiles and mis-enrols). Deriving the prose means
///         a rule and its explanation cannot disagree.
///     </para>
///     <para>
///         The grammar covered is the one the shipped workflows use: <c>&amp;&amp;</c>, <c>||</c>,
///         parentheses, comparisons, the keyed fact accessors, policy members, <c>ALevelGrade</c>
///         constants and the eligibility pass-count lambda. Anything outside it throws
///         <see cref="CriteriaNarrationException" /> — a rule that cannot be explained must fail loudly at
///         build time, never be silently dropped from the criteria a student reads.
///     </para>
/// </summary>
public static class ExpressionNarrator
{
	// Policy members are resolved by name off the loaded thresholds record rather than a second hard-coded
	// table, so retuning thresholds.yaml moves the prose and adding a knob cannot leave the two out of step.
	private static readonly FrozenDictionary<string, PropertyInfo> PolicyMembers =
		typeof(PolicyThresholds)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(static property => property.PropertyType == typeof(int) || property.PropertyType == typeof(double))
			.ToFrozenDictionary(static property => property.Name, StringComparer.Ordinal);

	private static readonly FrozenDictionary<double, string> ALevelGrades = new Dictionary<double, string> {
		[ALevelGrade.AStar] = "A*",
		[ALevelGrade.A] = "A",
		[ALevelGrade.B] = "B",
		[ALevelGrade.C] = "C",
		[ALevelGrade.D] = "D",
		[ALevelGrade.E] = "E",
		[ALevelGrade.U] = "U",
	}.ToFrozenDictionary();

	private static readonly IReadOnlyDictionary<string, string> EmptyLocals =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>
	///     Narrate <paramref name="expression" /> as one bullet per top-level <c>&amp;&amp;</c> conjunct —
	///     the shape the tier rules are authored in, so each bullet is one thing the student must satisfy.
	/// </summary>
	/// <param name="expression">The rule (or local-parameter) expression to narrate.</param>
	/// <param name="thresholds">The loaded policy the expression's members resolve against.</param>
	/// <param name="localParams">
	///     The rule's <c>LocalParams</c> by name, so a bullet built on one (the eligibility pass count)
	///     narrates the underlying computation instead of an opaque identifier.
	/// </param>
	/// <exception cref="CriteriaNarrationException">The expression uses a construct the narrator cannot explain.</exception>
	public static IReadOnlyList<string> Narrate(
		string expression,
		PolicyThresholds thresholds,
		IReadOnlyDictionary<string, string>? localParams = null)
	{
		ArgumentNullException.ThrowIfNull(expression);
		ArgumentNullException.ThrowIfNull(thresholds);

		var context = new NarrationContext(thresholds, localParams ?? EmptyLocals);
		var node = Parser.Parse(expression);

		return node is AndNode and_
			? [.. and_.Parts.Select(part => Sentence(Phrase(part, context)))]
			: [Sentence(Phrase(node, context))];
	}

	/// <summary>Capitalise a clause phrase and terminate it, turning it into a standalone bullet.</summary>
	private static string Sentence(string phrase) =>
		phrase.Length == 0 ? phrase : string.Concat(char.ToUpperInvariant(phrase[0]).ToString(), phrase[1..], ".");

	// Clause phrases are produced lower-case-initial and unterminated so they compose inside an "Either …,
	// or …" alternative; Sentence() lifts one into a bullet. Phrases that open on a proper noun ("GCSE
	// Maths …") are already capitalised and pass through Sentence unchanged.
	private static string Phrase(Node node, NarrationContext context) =>
		node switch {
			OrNode or_ => Alternatives(or_, context),
			AndNode and_ => string.Join(" and ", and_.Parts.Select(part => Phrase(part, context))),
			ComparisonNode comparison => Comparison(comparison, context),
			CallNode call => StandaloneCall(call, context),
			_ => throw Unexplainable(node),
		};

	private static string Alternatives(OrNode node, NarrationContext context)
	{
		var parts = node.Parts.Select(part => Phrase(part, context)).ToArray();
		var body = parts.Length == 2
			? $"{parts[0]}, or {parts[1]}"
			: $"{string.Join(", ", parts[..^1])}, or {parts[^1]}";

		return $"either {body}";
	}

	private static string Comparison(ComparisonNode node, NarrationContext context)
	{
		// The left operand decides the sentence shape; the right is the bound it is measured against, so it
		// is rendered in whatever units that shape reads (a GCSE grade, an A-level grade, a percentage).
		var left = node.Left is IdentifierNode identifier ? context.Local(identifier.Name) : node.Left;

		return left switch {
			// The rating rules read GCSEs through `facts.Gcse`, the eligibility rules through `lookup.Grade`;
			// both are the same question to a student, so both narrate identically.
			CallNode { Owner: "facts", Method: "Gcse", Arguments: [StringNode subject] } =>
				$"GCSE {Naming.Display(subject.Value)} at {Bounded(node, context, GradeBar)}",

			CallNode { Owner: "lookup", Method: "Grade", Arguments: [StringNode subject] } =>
				$"GCSE {Naming.Display(subject.Value)} at {Bounded(node, context, GradeBar)}",

			CallNode { Owner: "facts", Method: "Predicted", Arguments: [StringNode subject] } =>
				$"you are predicted grade {Grade(node.Right, context)} or better in A-level {Naming.Display(subject.Value)}",

			CallNode { Owner: "facts", Method: "DfeProbabilityAtOrAbove", Arguments: [StringNode subject, var grade] } =>
				$"nationally, at least {Percentage(node.Right, context)} of students with GCSE results like yours went on to get "
				+ $"grade {Grade(grade, context)} or better in A-level {Naming.Display(subject.Value)}",

			CallNode { Owner: "gcses", Method: "Count", Arguments: [LambdaNode lambda] } =>
				$"at least {Number(node.Right, context)} GCSEs {CountedBy(lambda, context)}",

			MemberNode { Owner: "facts", Name: "Average" } =>
				$"an average GCSE grade of {Bounded(node, context, static (bound, inner) => Number(bound, inner))}",

			MemberNode { Owner: "facts", Name: "Age" } =>
				$"you are {Number(node.Right, context)} or older",

			_ => throw Unexplainable(left),
		};
	}

	/// <summary>
	///     Render a comparison's right-hand bound. A rule may make the bound itself conditional on a fact
	///     about the student (Art's entry grade is age-gated), in which case both branches are spelled out
	///     rather than one being silently reported as "the" requirement.
	/// </summary>
	private static string Bounded(ComparisonNode node, NarrationContext context, Func<Node, NarrationContext, string> render) =>
		node.Right is ConditionalNode conditional
			? $"{render(conditional.WhenTrue, context)} {Bound(node.Operator)} if {Phrase(conditional.Condition, context)}, "
			  + $"or {render(conditional.WhenFalse, context)} {Bound(node.Operator)} if not"
			: $"{render(node.Right, context)} {Bound(node.Operator)}";

	private static string GradeBar(Node bound, NarrationContext context) => $"grade {Number(bound, context)}";

	/// <summary>The predicate of the eligibility pass count, narrated as the grade bar it applies.</summary>
	private static string CountedBy(LambdaNode lambda, NarrationContext context) =>
		lambda.Body is ComparisonNode { Left: MemberNode { Name: "Grade" } } comparison
			? $"at grade {Number(comparison.Right, context)} {Bound(comparison.Operator)}"
			: throw Unexplainable(lambda.Body);

	private static string StandaloneCall(CallNode node, NarrationContext context) =>
		node switch {
			{ Owner: "facts", Method: "HasEntryEquivalent", Arguments: [StringNode subject] } =>
				$"a prior qualification that counts instead of GCSE {Naming.Display(subject.Value)}",
			_ => throw Unexplainable(node),
		};

	private static string Bound(string @operator) =>
		@operator switch {
			">=" => "or above",
			">" => "or above (higher than this)",
			"<=" => "or below",
			"<" => "or below (lower than this)",
			"==" => "exactly",
			_ => throw new CriteriaNarrationException($"comparison operator '{@operator}' has no plain-English form"),
		};

	private static string Number(Node node, NarrationContext context) =>
		Value(node, context).ToString("0.##", CultureInfo.InvariantCulture);

	private static string Percentage(Node node, NarrationContext context) =>
		(Value(node, context) * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

	private static string Grade(Node node, NarrationContext context)
	{
		var points = Value(node, context);
		return ALevelGrades.TryGetValue(points, out var grade)
			? grade
			: throw new CriteriaNarrationException(
				$"{points.ToString("0.##", CultureInfo.InvariantCulture)} is not a point on the A-level grade scale");
	}

	private static double Value(Node node, NarrationContext context) =>
		node switch {
			NumberNode number => number.Value,
			MemberNode { Owner: "ALevelGrade" } grade => Constant(typeof(ALevelGrade), grade.Name),
			MemberNode { Owner: "Thresholds" } threshold => Constant(typeof(Thresholds), threshold.Name),
			MemberNode member when PolicyMembers.TryGetValue(member.Name, out var property) =>
				Convert.ToDouble(property.GetValue(context.Thresholds), CultureInfo.InvariantCulture),
			_ => throw Unexplainable(node),
		};

	private static double Constant(Type owner, string name) =>
		owner.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is { } value
			? Convert.ToDouble(value, CultureInfo.InvariantCulture)
			: throw new CriteriaNarrationException($"'{owner.Name}.{name}' is not a known constant");

	private static CriteriaNarrationException Unexplainable(Node node) =>
		new($"cannot explain '{node.Describe()}' in plain English; teach {nameof(ExpressionNarrator)} this rule shape");

	private sealed class NarrationContext(PolicyThresholds thresholds, IReadOnlyDictionary<string, string> localParams)
	{
		public PolicyThresholds Thresholds { get; } = thresholds;

		/// <summary>Expand a local-parameter reference to the expression it stands for.</summary>
		public Node Local(string name) =>
			localParams.TryGetValue(name, out var expression)
				? Parser.Parse(expression)
				: throw new CriteriaNarrationException($"expression references unknown local parameter '{name}'");
	}
}

/// <summary>
///     A rule expression the narrator cannot render as English. Thrown rather than swallowed: an
///     unexplainable rule silently missing from a student's criteria is the failure this whole feature
///     exists to avoid.
/// </summary>
public sealed class CriteriaNarrationException(string message) : Exception(message);
