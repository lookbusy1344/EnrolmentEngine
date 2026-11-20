namespace EnrolmentRules.Engine.Authoring;

using Domain;

/// <summary>
///     Evaluates a parsed rating-rule expression against one student's <see cref="RatingFacts" /> in host code,
///     so a shortfall message can test each clause on its own. Covers the rating-rule grammar
///     <see cref="ExpressionNarrator" /> covers. <see cref="Validate" /> rejects any other shape when the engine
///     is built, and <see cref="ShortfallExplainer" /> checks every verdict it explains against RulesEngine's.
/// </summary>
internal static class RuleInterpreter
{
	public static bool Holds(Node node, RatingFacts facts) =>
		node switch {
			AndNode and_ => and_.Parts.All(part => Holds(part, facts)),
			OrNode or_ => or_.Parts.Any(part => Holds(part, facts)),
			ComparisonNode comparison => Compare(Value(comparison.Left, facts), comparison.Operator, Value(comparison.Right, facts)),
			CallNode { Owner: "facts", Method: "HasEntryEquivalent", Arguments: [StringNode subject] } => facts.HasEntryEquivalent(subject.Value),
			BooleanNode boolean => boolean.Value,
			_ => throw Unsupported(node),
		};

	public static double Value(Node node, RatingFacts facts) =>
		node switch {
			NumberNode number => number.Value,
			CallNode { Owner: "facts", Method: "Gcse", Arguments: [StringNode subject] } => facts.Gcse(subject.Value),
			CallNode { Owner: "facts", Method: "Predicted", Arguments: [StringNode subject] } => facts.Predicted(subject.Value),
			CallNode { Owner: "facts", Method: "DfeProbabilityAtOrAbove", Arguments: [StringNode subject, var grade] } =>
				facts.DfeProbabilityAtOrAbove(subject.Value, Value(grade, facts)),
			MemberNode { Owner: "facts" } member => Member(member, facts),
			MemberNode { Owner: "ALevelGrade" } grade => ExpressionNarrator.Constant(typeof(ALevelGrade), grade.Name),
			MemberNode { Owner: "Thresholds" } threshold => ExpressionNarrator.Constant(typeof(Thresholds), threshold.Name),
			ConditionalNode conditional => Holds(conditional.Condition, facts)
				? Value(conditional.WhenTrue, facts)
				: Value(conditional.WhenFalse, facts),
			_ => throw Unsupported(node),
		};

	/// <summary>Throws <see cref="CriteriaNarrationException" /> unless every node in the tree is one the interpreter evaluates.</summary>
	public static void Validate(Node node)
	{
		switch (node) {
			case AndNode and_:
				and_.Parts.ToList().ForEach(Validate);
				break;
			case OrNode or_:
				or_.Parts.ToList().ForEach(Validate);
				break;
			case ComparisonNode comparison:
				_ = Compare(0, comparison.Operator, 0);
				ValidateValue(comparison.Left);
				ValidateValue(comparison.Right);
				break;
			case CallNode { Owner: "facts", Method: "HasEntryEquivalent", Arguments: [StringNode] }:
			case BooleanNode:
				break;
			default:
				throw Unsupported(node);
		}
	}

	private static void ValidateValue(Node node)
	{
		switch (node) {
			case NumberNode:
			case CallNode { Owner: "facts", Method: "Gcse" or "Predicted", Arguments: [StringNode] }:
				break;
			case CallNode { Owner: "facts", Method: "DfeProbabilityAtOrAbove", Arguments: [StringNode, var grade] }:
				ValidateValue(grade);
				break;
			case MemberNode {
				Owner: "facts", Name: nameof(RatingFacts.Average) or nameof(RatingFacts.Age) or nameof(RatingFacts.TopEntry)
				or nameof(RatingFacts.PassGrade) or nameof(RatingFacts.StandardEntry) or nameof(RatingFacts.ExceptionalEntry)
				or nameof(RatingFacts.MinDfeGreenProbabilityAtOrAbove) or nameof(RatingFacts.MinDfeAmberProbabilityAtOrAbove)
			}:
				break;
			case MemberNode { Owner: "ALevelGrade" } grade:
				_ = ExpressionNarrator.Constant(typeof(ALevelGrade), grade.Name);
				break;
			case MemberNode { Owner: "Thresholds" } threshold:
				_ = ExpressionNarrator.Constant(typeof(Thresholds), threshold.Name);
				break;
			case ConditionalNode conditional:
				Validate(conditional.Condition);
				ValidateValue(conditional.WhenTrue);
				ValidateValue(conditional.WhenFalse);
				break;
			default:
				throw Unsupported(node);
		}
	}

	private static double Member(MemberNode member, RatingFacts facts) =>
		member.Name switch {
			nameof(RatingFacts.Average) => facts.Average,
			nameof(RatingFacts.Age) => facts.Age,
			nameof(RatingFacts.TopEntry) => facts.TopEntry,
			nameof(RatingFacts.PassGrade) => facts.PassGrade,
			nameof(RatingFacts.StandardEntry) => facts.StandardEntry,
			nameof(RatingFacts.ExceptionalEntry) => facts.ExceptionalEntry,
			nameof(RatingFacts.MinDfeGreenProbabilityAtOrAbove) => facts.MinDfeGreenProbabilityAtOrAbove,
			nameof(RatingFacts.MinDfeAmberProbabilityAtOrAbove) => facts.MinDfeAmberProbabilityAtOrAbove,
			_ => throw Unsupported(member),
		};

	private static bool Compare(double left, string @operator, double right) =>
		@operator switch {
			">=" => left >= right,
			">" => left > right,
			"<=" => left <= right,
			"<" => left < right,
			"==" => left == right,
			"!=" => left != right,
			_ => throw new CriteriaNarrationException($"comparison operator '{@operator}' is not supported."),
		};

	private static CriteriaNarrationException Unsupported(Node node) =>
		new($"cannot evaluate '{node.Describe()}' in host code; teach {nameof(RuleInterpreter)} this rule shape.");
}
