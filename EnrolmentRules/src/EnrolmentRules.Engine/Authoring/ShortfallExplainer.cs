namespace EnrolmentRules.Engine.Authoring;

using System.Collections.Frozen;
using System.Globalization;
using Domain;
using Prediction;
using RulesEngine.Models;

/// <summary>
///     Builds <see cref="Explanation.Message" />: for an amber or red engine verdict, the clauses of the next
///     rating's rule that the student fails, each with the student's own value, plus the lowest GCSE average
///     that would pass that rule when raising the average alone can do it. Clauses are evaluated by
///     <see cref="RuleInterpreter" />; every message is checked against the engine's verdict, and a
///     disagreement throws. A rule outside the interpreter's grammar keeps the deciding reason as its message.
/// </summary>
internal sealed class ShortfallExplainer
{
	private const string AllMet = "All requirements met.";

	// The average search and the floored "yours" values work in tenths of a GCSE grade.
	private const int TenthsPerGrade = 10;
	private const int PercentScale = 100;

	// Absorbs binary rounding before flooring, so an average of 4.3 floors to 4.3 and not 4.2.
	private const double FloorTolerance = 1e-9;

	private readonly CatalogueData catalogue;
	private readonly DfeTransitionMatrix matrix;
	private readonly PolicyFacts policy;
	private readonly FrozenDictionary<string, Node?> rules;
	private readonly QualificationScale scale;

	public ShortfallExplainer(
		IReadOnlyList<Workflow> workflows,
		PolicyThresholds thresholds,
		CatalogueData catalogue,
		QualificationScale scale,
		DfeTransitionMatrix matrix)
	{
		rules = workflows
				.Where(static workflow => string.Equals(workflow.WorkflowName, RatingEvaluator.SubjectRatingsWorkflow, StringComparison.Ordinal))
				.SelectMany(static workflow => workflow.Rules)
				.ToFrozenDictionary(static rule => rule.RuleName, static rule => Interpretable(rule.Expression ?? string.Empty), StringComparer.Ordinal);
		UninterpretableRules = [.. rules.Where(static pair => pair.Value is null).Select(static pair => pair.Key).Order(StringComparer.Ordinal)];
		policy = new(thresholds);
		this.catalogue = catalogue;
		this.scale = scale;
		this.matrix = matrix;
	}

	/// <summary>The rating rules whose message falls back to the deciding reason, by name.</summary>
	public IReadOnlyList<string> UninterpretableRules { get; }

	/// <summary>One message per subject for a single pipeline run.</summary>
	public IReadOnlyDictionary<Subject, string> Messages(Evaluation evaluation)
	{
		var session = new Session(this, evaluation);
		var baseBySubject = evaluation.BaseRatings.ToDictionary(static r => r.Subject);
		return evaluation.FinalRatings.ToDictionary(static r => r.Subject, final => session.Message(baseBySubject[final.Subject], final));
	}

	private static Node? Interpretable(string expression)
	{
		try {
			var node = Parser.Parse(expression);
			RuleInterpreter.Validate(node);
			return node is BooleanNode || Clauses(node).All(Explainable) ? node : null;
		}
		catch (CriteriaNarrationException) {
			return null;
		}
	}

	/// <summary>Whether <see cref="Phrase" /> can word this clause.</summary>
	private static bool Explainable(Node clause) =>
		clause switch {
			AndNode and_ => and_.Parts.All(Explainable),
			OrNode or_ => or_.Parts.All(Explainable),
			ComparisonNode { Left: CallNode { Method: "Gcse" or "Predicted" or "DfeProbabilityAtOrAbove" } } => true,
			ComparisonNode { Left: MemberNode { Name: nameof(RatingFacts.Average) or nameof(RatingFacts.Age) } } => true,
			CallNode { Method: "HasEntryEquivalent" } => true,
			_ => false,
		};

	private static IReadOnlyList<Node> Clauses(Node rule) => rule is AndNode and_ ? and_.Parts : [rule];

	private static string Phrase(Node clause, RatingFacts facts) =>
		clause switch {
			AndNode and_ => string.Join(" and ", and_.Parts.Where(part => !RuleInterpreter.Holds(part, facts)).Select(part => Phrase(part, facts))),
			OrNode or_ => $"either {Alternatives([.. or_.Parts.Select(part => Phrase(part, facts))])}",
			ComparisonNode comparison => Comparison(comparison, facts),
			CallNode { Owner: "facts", Method: "HasEntryEquivalent", Arguments: [StringNode subject] } =>
				$"a prior qualification that counts instead of GCSE {Naming.Display(subject.Value)}",
			_ => throw Unexplainable(clause),
		};

	private static string Alternatives(IReadOnlyList<string> parts) =>
		parts.Count == 2 ? $"{parts[0]}, or {parts[1]}" : $"{string.Join(", ", parts.Take(parts.Count - 1))}, or {parts[^1]}";

	private static string Comparison(ComparisonNode node, RatingFacts facts)
	{
		var bound = RuleInterpreter.Value(node.Right, facts);
		var actual = RuleInterpreter.Value(node.Left, facts);
		var direction = ExpressionNarrator.Bound(node.Operator);

		return node.Left switch {
			CallNode { Method: "Gcse", Arguments: [StringNode subject] } =>
				$"GCSE {Naming.Display(subject.Value)} at grade {Whole(bound)} {direction} ({Held(actual)})",
			CallNode { Method: "Predicted" } =>
				$"a predicted grade {ALevelGrade.Name(bound)} or better (yours: {FlooredGrade(actual)})",
			CallNode { Method: "DfeProbabilityAtOrAbove", Arguments: [_, var grade] } =>
				$"at least {Whole(bound * PercentScale)}% of similar students nationally to reach grade "
				+ $"{ALevelGrade.Name(RuleInterpreter.Value(grade, facts))} or better (yours: {Floored(actual * PercentScale)}%)",
			MemberNode { Name: nameof(RatingFacts.Average) } =>
				$"a GCSE average of {Tenths(bound)} {direction} (yours: {Tenths(FlooredTenths(actual))})",
			MemberNode { Name: nameof(RatingFacts.Age) } => $"age {Whole(bound)} {direction} (yours: {Whole(actual)})",
			_ => throw Unexplainable(node),
		};
	}

	/// <summary>Whether raising the GCSE average, every individual grade held, can turn this failed clause true.</summary>
	private static bool AverageCanFix(Node clause, RatingFacts facts) =>
		clause switch {
			AndNode and_ => and_.Parts.Where(part => !RuleInterpreter.Holds(part, facts)).All(part => AverageCanFix(part, facts)),
			OrNode or_ => or_.Parts.Any(part => AverageCanFix(part, facts)),
			ComparisonNode { Left: MemberNode { Name: nameof(RatingFacts.Average) } or CallNode { Method: "Predicted" or "DfeProbabilityAtOrAbove" } } => true,
			_ => false,
		};

	private static bool IsAverageComparison(Node clause) =>
		clause is ComparisonNode { Left: MemberNode { Owner: "facts", Name: nameof(RatingFacts.Average) } };

	private static string Held(double grade) => grade < Thresholds.MinGcseGrade ? "not taken" : $"yours: {Whole(grade)}";

	private static string FlooredGrade(double points) =>
		ALevelGrade.Name(Math.Clamp(Math.Floor(points + FloorTolerance), ALevelGrade.Min, ALevelGrade.Max));

	private static double FlooredTenths(double value) => Math.Floor((value * TenthsPerGrade) + FloorTolerance) / TenthsPerGrade;

	private static string Floored(double value) => Whole(Math.Floor(value + FloorTolerance));

	private static string Whole(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

	private static string Tenths(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

	private static string Title(Rating rating) => rating == Rating.Green ? "Green" : "Amber";

	private static CriteriaNarrationException Unexplainable(Node node) =>
		new($"cannot explain the shortfall '{node.Describe()}'; teach {nameof(ShortfallExplainer)} this rule shape.");

	/// <summary>One pipeline run's facts, with the hypothetical-average facts cached across subjects.</summary>
	private sealed class Session
	{
		private readonly Dictionary<int, RatingFacts> byTenths = [];
		private readonly RatingFacts current;
		private readonly Evaluation evaluation;
		private readonly GcseFacts lookup;
		private readonly ShortfallExplainer owner;

		public Session(ShortfallExplainer owner, Evaluation evaluation)
		{
			this.owner = owner;
			this.evaluation = evaluation;
			lookup = new(evaluation.Gcses);
			current = new(evaluation.Profile, lookup, owner.policy, owner.catalogue, owner.scale);
		}

		public string Message(SubjectRating @base, SubjectRating final)
		{
			if (!evaluation.Gate.Eligible || final.Rating != @base.Rating || !string.Equals(final.Reason, @base.Reason, StringComparison.Ordinal)) {
				return final.Reason;
			}

			var deciding = Rule(@base.Subject, @base.Rating);
			if (deciding is null) {
				return final.Reason;
			}

			if (!RuleInterpreter.Holds(deciding, current)) {
				throw Disagreement(@base.Subject, @base.Rating, "holds");
			}

			if (@base.Rating == Rating.Green) {
				return AllMet;
			}

			var target = @base.Rating == Rating.Red && owner.rules.ContainsKey(RatingEvaluator.RuleName(@base.Subject, Rating.Amber))
				? Rating.Amber
				: Rating.Green;
			var rule = Rule(@base.Subject, target);
			if (rule is null) {
				return final.Reason;
			}

			var failed = Clauses(rule).Where(clause => !RuleInterpreter.Holds(clause, current)).ToList();
			if (failed.Count == 0) {
				throw Disagreement(@base.Subject, target, "fails");
			}

			var needs = $"{Title(target)} needs {string.Join("; ", failed.Select(clause => Phrase(clause, current)))}.";
			var average = failed.All(IsAverageComparison) || !failed.All(clause => AverageCanFix(clause, current))
				? null
				: LowestAverage(rule);

			return average is double lowest
				? FormattableString.Invariant(
					$"{needs} A GCSE average of {Tenths(lowest)} would get {EnumNames.NameOf(target)} (yours: {Tenths(FlooredTenths(current.Average))}).")
				: needs;
		}

		private Node? Rule(Subject subject, Rating rating)
		{
			var name = RatingEvaluator.RuleName(subject, rating);
			return owner.rules.TryGetValue(name, out var rule)
				? rule
				: throw new InvalidOperationException($"no '{name}' rule is loaded to explain.");
		}

		private double? LowestAverage(Node rule)
		{
			var first = (int)Math.Floor((current.Average * TenthsPerGrade) + FloorTolerance) + 1;
			for (var tenths = first; tenths <= Thresholds.MaxGcseGrade * TenthsPerGrade; ++tenths) {
				if (RuleInterpreter.Holds(rule, At(tenths))) {
					return (double)tenths / TenthsPerGrade;
				}
			}

			return null;
		}

		private RatingFacts At(int tenths)
		{
			if (!byTenths.TryGetValue(tenths, out var facts)) {
				var profile = GradePredictor.Predict(
					evaluation.Student, evaluation.Gcses, (double)tenths / TenthsPerGrade, evaluation.AsOf, owner.catalogue, owner.matrix, owner.scale);
				facts = new(profile, lookup, owner.policy, owner.catalogue, owner.scale);
				byTenths[tenths] = facts;
			}

			return facts;
		}

		private static InvalidOperationException Disagreement(Subject subject, Rating rating, string engineVerdict) =>
			new($"The host-code reading of rule '{RatingEvaluator.RuleName(subject, rating)}' disagrees with RulesEngine, "
				+ $"which found that it {engineVerdict}.");
	}
}
