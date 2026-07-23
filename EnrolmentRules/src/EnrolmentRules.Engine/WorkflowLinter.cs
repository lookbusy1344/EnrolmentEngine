namespace EnrolmentRules.Engine.Authoring;

using System.Reflection;
using System.Text.RegularExpressions;
using Domain;
using RulesEngine.Models;

/// <summary>
///     Static structural checks over already-loaded workflows. This complements schema validation and
///     probe compilation by catching semantic drift that only becomes visible from the rule graph itself.
/// </summary>
[CLSCompliant(false)]
public static partial class WorkflowLinter
{
	private static readonly Dictionary<string, Type> MemberOwners = new(StringComparer.Ordinal) {
		["facts"] = typeof(RatingFacts),
		["lookup"] = typeof(GcseFacts),
		["policy"] = typeof(PolicyFacts),
		["Thresholds"] = typeof(Thresholds),
		["ALevelGrade"] = typeof(ALevelGrade),
	};

	// The subject-keyed accessors whose first string-literal argument is a subject key. RulesEngine
	// lambdas are untyped (Reservation 1), so a typo'd key here compiles, binds and silently returns the
	// not-taken sentinel (grade 0 / U) — a permanent wrong red. The schema and member checks never see
	// inside the string, so the key is validated here against the right vocabulary. The two vocabularies
	// deliberately differ: GcseSubjects carries english_language (no A-level) and omits further_maths.
	private static readonly Dictionary<(string Owner, string Method), KeyVocabulary> KeyedAccessors = new() {
		[("facts", "Gcse")] = KeyVocabulary.Gcse,
		[("lookup", "Grade")] = KeyVocabulary.Gcse,
		[("facts", "Predicted")] = KeyVocabulary.Subject,
		[("facts", "DfeProbabilityAtOrAbove")] = KeyVocabulary.Subject,
	};

	// The DfE confidence floors the two tiers read. Sourced by nameof so a rename of the RatingFacts member
	// carries the linter with it rather than drifting from a hard-coded string.
	private static readonly string GreenDfeFloor = nameof(RatingFacts.MinDfeGreenProbabilityAtOrAbove);
	private static readonly string AmberDfeFloor = nameof(RatingFacts.MinDfeAmberProbabilityAtOrAbove);

	/// <summary>Lint loaded workflows against an explicit catalogue snapshot.</summary>
	public static IReadOnlyList<LintFinding> Lint(IReadOnlyList<Workflow> workflows, CatalogueData catalogue)
	{
		var findings = new List<LintFinding>();
		foreach (var workflow in workflows) {
			findings.AddRange(LintWorkflow(workflow, catalogue));
		}

		return findings;
	}

	private static IEnumerable<LintFinding> LintWorkflow(Workflow workflow, CatalogueData catalogue)
	{
		var rules = Flatten(workflow.Rules).ToArray();
		foreach (var finding in LintExpressions(workflow.WorkflowName, rules, catalogue)) {
			yield return finding;
		}

		if (workflow.WorkflowName == RatingEvaluator.SubjectRatingsWorkflow) {
			foreach (var finding in LintSubjectRatings(workflow.WorkflowName, rules, catalogue)) {
				yield return finding;
			}
		}

		if (workflow.WorkflowName == RatingEvaluator.EligibilityWorkflow) {
			foreach (var finding in LintEligibility(workflow.WorkflowName, rules)) {
				yield return finding;
			}
		}
	}

	private static IEnumerable<LintFinding> LintSubjectRatings(string workflowName, Rule[] rules, CatalogueData catalogue)
	{
		var parsedRules = new Dictionary<Subject, List<(Rating Rating, int Index, Rule Rule)>>();

		for (var index = 0; index < rules.Length; index++) {
			var rule = rules[index];
			if (!TryParseSubjectRatingRule(rule.RuleName, out var subject, out var rating)) {
				yield return new(
					workflowName,
					rule.RuleName,
					LintSeverity.Error,
					$"rule name '{rule.RuleName}' must match '<subject>:<rating>'");
				continue;
			}

			if (!parsedRules.TryGetValue(subject, out var entries)) {
				entries = [];
				parsedRules[subject] = entries;
			}

			entries.Add((rating, index, rule));
		}

		foreach (var subject in catalogue.Subjects) {
			if (!parsedRules.TryGetValue(subject, out var subjectRules)) {
				subjectRules = [];
			}

			var byRating = subjectRules
				.GroupBy(static entry => entry.Rating)
				.ToDictionary(static group => group.Key, static group => group.OrderBy(static entry => entry.Index).ToArray());

			foreach (var rating in Enum.GetValues<Rating>()) {
				if (!byRating.TryGetValue(rating, out var matches)) {
					yield return new(
						workflowName,
						RatingEvaluator.RuleName(subject, rating),
						LintSeverity.Error,
						$"{EnumNames.NameOf(subject)} is missing its {EnumNames.NameOf(rating)} rule");
					continue;
				}

				if (matches.Length > 1) {
					yield return new(
						workflowName,
						RatingEvaluator.RuleName(subject, rating),
						LintSeverity.Error,
						$"{EnumNames.NameOf(subject)} has {matches.Length} {EnumNames.NameOf(rating)} rules");
				}
			}

			var declaredRatings = subjectRules.OrderBy(static entry => entry.Index).Select(static entry => entry.Rating).ToArray();
			var severityOrder = declaredRatings.OrderBy(static rating => (int)rating).ToArray();
			if (!declaredRatings.SequenceEqual(severityOrder)) {
				yield return new(
					workflowName,
					EnumNames.NameOf(subject),
					LintSeverity.Error,
					$"{EnumNames.NameOf(subject)} rules must be ordered green → amber → red");
			}

			if (byRating.TryGetValue(Rating.Red, out var redRules)) {
				foreach (var redRule in redRules) {
					if (!IsCatchAllTrue(redRule.Rule.Expression)) {
						yield return new(
							workflowName,
							redRule.Rule.RuleName,
							LintSeverity.Error,
							$"{redRule.Rule.RuleName} must be an unconditional true catch-all");
					}
				}
			}

			// The green tier must be strictly stronger than the amber tier. The subject-ratings YAML is a
			// large copy-paste surface (Reservation 1): a green rule left comparing to the amber predicted
			// grade, or wired to the amber DfE confidence floor, compiles, orders and produces a rating — but
			// silently promotes amber-level students to green. The ordering/existence checks above never see
			// inside the expressions, so the tier boundary is pinned here from the green/amber pair itself.
			if (byRating.TryGetValue(Rating.Green, out var greenRules) && greenRules.Length == 1
																	   && byRating.TryGetValue(Rating.Amber, out var amberRules) &&
																	   amberRules.Length == 1) {
				foreach (var finding in LintTierStrength(workflowName, subject, greenRules[0].Rule, amberRules[0].Rule)) {
					yield return finding;
				}
			}
		}

		foreach (var subject in parsedRules.Keys
					 .Where(subject => !catalogue.Subjects.Contains(subject))
					 .OrderBy(static subject => EnumNames.NameOf(subject), StringComparer.Ordinal)) {
			yield return new(
				workflowName,
				EnumNames.NameOf(subject),
				LintSeverity.Error,
				$"rules exist for subject '{EnumNames.NameOf(subject)}' which is not in the catalogue; they will never produce a rating");
		}
	}

	private static IEnumerable<LintFinding> LintTierStrength(string workflowName, Subject subject, Rule green, Rule amber)
	{
		var subjectName = EnumNames.NameOf(subject);

		// The predicted-grade tier boundary: green must demand at least as high a predicted grade as amber.
		// Equal grades are allowed (a subject may legitimately separate the tiers on DfE confidence alone), so
		// only a green tier that is strictly weaker than its amber tier is a defect.
		if (PredictedThreshold(green.Expression) is (var greenToken, var greenValue)
			&& PredictedThreshold(amber.Expression) is (var amberToken, var amberValue)
			&& greenValue < amberValue) {
			yield return new(
				workflowName,
				green.RuleName,
				LintSeverity.Error,
				$"{subjectName} green tier requires a weaker predicted grade (ALevelGrade.{greenToken}) than its amber tier (ALevelGrade.{amberToken}); green must be at least as strong");
		}

		// The DfE confidence floors must not be transposed: green reads the green floor, amber the amber floor.
		if (References(green.Expression, AmberDfeFloor)) {
			yield return new(
				workflowName,
				green.RuleName,
				LintSeverity.Error,
				$"{subjectName} green tier reads the amber DfE confidence floor '{AmberDfeFloor}'; it must read '{GreenDfeFloor}'");
		}

		if (References(amber.Expression, GreenDfeFloor)) {
			yield return new(
				workflowName,
				amber.RuleName,
				LintSeverity.Error,
				$"{subjectName} amber tier reads the green DfE confidence floor '{GreenDfeFloor}'; it must read '{AmberDfeFloor}'");
		}
	}

	// The first `Predicted(...) >= ALevelGrade.X` threshold in an expression, resolved to its scale value for
	// comparison. Null when the rule carries no predicted-grade comparison (e.g. an entry-only tier), which
	// simply opts that pair out of the grade check.
	private static (string Token, double Value)? PredictedThreshold(string? expression)
	{
		if (expression is null) {
			return null;
		}

		var match = PredictedThresholdRegex().Match(expression);
		if (!match.Success) {
			return null;
		}

		var token = match.Groups["grade"].Value;
		return typeof(ALevelGrade).GetField(token, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is double value
			? (token, value)
			: null;
	}

	private static bool References(string? expression, string member) =>
		expression is not null && expression.Contains(member, StringComparison.Ordinal);

	private static IEnumerable<LintFinding> LintEligibility(string workflowName, IReadOnlyList<Rule> rules)
	{
		var expected = new[] { "EnglishLanguagePass", "MathsPass", "EnoughPasses" };
		var seen = rules.Select(static rule => rule.RuleName).ToArray();

		if (!seen.SequenceEqual(expected, StringComparer.Ordinal)) {
			yield return new(
				workflowName,
				RatingEvaluator.EligibilityWorkflow,
				LintSeverity.Error,
				"eligibility must contain exactly EnglishLanguagePass, MathsPass and EnoughPasses in that order");
		}

		foreach (var ruleName in seen.Where(ruleName => !expected.Contains(ruleName, StringComparer.Ordinal))) {
			yield return new(
				workflowName,
				ruleName,
				LintSeverity.Error,
				$"eligibility contains unexpected rule '{ruleName}'");
		}

		foreach (var ruleName in expected.Except(seen, StringComparer.Ordinal)) {
			yield return new(
				workflowName,
				ruleName,
				LintSeverity.Error,
				$"eligibility is missing required rule '{ruleName}'");
		}
	}

	private static IEnumerable<LintFinding> LintExpressions(string workflowName, IReadOnlyList<Rule> rules, CatalogueData catalogue)
	{
		foreach (var rule in rules) {
			foreach (var expression in Expressions(rule)) {
				foreach (var finding in LintExpression(workflowName, rule.RuleName, expression, catalogue)) {
					yield return finding;
				}
			}
		}
	}

	private static IEnumerable<LintFinding> LintExpression(string workflowName, string? ruleName, string expression, CatalogueData catalogue)
	{
		foreach (Match match in MemberAccessRegex().Matches(expression)) {
			if (!MemberOwners.TryGetValue(match.Groups["owner"].Value, out var ownerType)) {
				continue;
			}

			var memberName = match.Groups["member"].Value;
			if (ownerType.GetMember(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length > 0) {
				continue;
			}

			yield return new(
				workflowName,
				ruleName,
				LintSeverity.Error,
				$"expression references unknown member '{match.Groups["owner"].Value}.{memberName}'");
		}

		foreach (Match match in SubjectKeyRegex().Matches(expression)) {
			var accessor = (match.Groups["owner"].Value, match.Groups["method"].Value);
			if (!KeyedAccessors.TryGetValue(accessor, out var vocabulary)) {
				continue;
			}

			var key = match.Groups["key"].Value;
			var known = vocabulary switch {
				KeyVocabulary.Gcse => GcseSubjects.IsKnown(key),
				KeyVocabulary.Subject => Subject.TryParse(key, out var subject) && catalogue.Subjects.Contains(subject),
				_ => true,
			};

			if (!known) {
				yield return new(
					workflowName,
					ruleName,
					LintSeverity.Error,
					$"expression references unknown subject key '{key}' in {accessor.Item1}.{accessor.Item2}(...)");
			}
		}
	}

	private static bool IsCatchAllTrue(string? expression) =>
		string.Equals(expression?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

	private static bool TryParseSubjectRatingRule(string? ruleName, out Subject subject, out Rating rating)
	{
		subject = default;
		rating = default;

		if (string.IsNullOrWhiteSpace(ruleName)) {
			return false;
		}

		var parts = ruleName.Split(RatingEvaluator.RuleNameSeparator, 2);
		return parts is [var subjectName, var ratingName]
			   && EnumNames.TryParse(subjectName, out subject)
			   && EnumNames.TryParse(ratingName, out rating);
	}

	private static IEnumerable<Rule> Flatten(IEnumerable<Rule>? rules)
	{
		if (rules is null) {
			yield break;
		}

		foreach (var rule in rules) {
			yield return rule;

			foreach (var child in Flatten(rule.Rules)) {
				yield return child;
			}
		}
	}

	private static IEnumerable<string> Expressions(Rule rule)
	{
		if (!string.IsNullOrWhiteSpace(rule.Expression)) {
			yield return rule.Expression!;
		}

		if (rule.LocalParams is not null) {
			foreach (var param in rule.LocalParams) {
				if (!string.IsNullOrWhiteSpace(param.Expression)) {
					yield return param.Expression!;
				}
			}
		}
	}

	[GeneratedRegex(@"(?<![\w])(?<owner>[A-Za-z_][A-Za-z0-9_]*)\.(?<member>[A-Za-z_][A-Za-z0-9_]*)")]
	private static partial Regex MemberAccessRegex();

	// Match a subject-keyed accessor call and capture its first string-literal argument: the subject key.
	[GeneratedRegex(@"(?<![\w])(?<owner>facts|lookup)\.(?<method>Gcse|Predicted|DfeProbabilityAtOrAbove|Grade)\(\s*""(?<key>[^""]*)""")]
	private static partial Regex SubjectKeyRegex();

	// Match a `Predicted(...) >= ALevelGrade.X` predicted-grade tier comparison and capture the grade token.
	[GeneratedRegex(@"Predicted\s*\([^)]*\)\s*>=\s*ALevelGrade\.(?<grade>[A-Za-z]+)")]
	private static partial Regex PredictedThresholdRegex();

	private enum KeyVocabulary
	{
		/// <summary>A GCSE subject key, validated against <see cref="GcseSubjects.Known" />.</summary>
		Gcse,

		/// <summary>An A-level <see cref="Subject" /> name, validated against the type.</summary>
		Subject,
	}
}
