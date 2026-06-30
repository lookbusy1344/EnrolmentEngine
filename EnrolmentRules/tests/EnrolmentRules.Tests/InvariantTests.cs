namespace EnrolmentRules.Tests;

using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Domain;
using Engine;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

/// <summary>
///     Phase 9 — property tests and workflow/catalogue drift. The invariants here are the executable
///     contract for the acyclic, monotone design: random valid students must never throw, the final
///     verdict must preserve the finite rating lattice, and the shipped workflow YAML must stay aligned
///     with the catalogue as the single source of truth.
/// </summary>
public sealed partial class InvariantTests : IAsyncLifetime
{
	// With the green cap disabled (the shipped default), the projected tariff is maximised when every
	// subject is green — each contributing its full UCAS weight, with no amber discount. That sum is the
	// upper bound any real student's tariff must stay within.
	private static readonly double MaxProjectedTariff =
		Catalogue.Subjects.Sum(static subject => Catalogue.Meta(subject).UcasWeight);

	private EnrolmentEngine engine = null!;

	public async Task InitializeAsync() => engine = await Harness.ShippedEngineAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public void every_catalogue_subject_has_a_named_rule_group_in_subject_ratings()
	{
		using var doc = WorkflowDocument("subject-ratings.yaml");

		var rules = doc.RootElement.GetProperty("Rules").EnumerateArray()
			.Select(static rule => rule.GetProperty("RuleName").GetString())
			.ToArray();

		rules.Should().OnlyContain(static name => !string.IsNullOrWhiteSpace(name));

		var subjectNames = rules
			.Select(static name => name!.Split(':', 2)[0])
			.ToArray();
		var parsedSubjects = subjectNames
			.Select(static name => Subject.TryParse(name, out var subject) ? subject : throw new InvalidOperationException(name))
			.ToArray();

		subjectNames.Should().OnlyContain(name => IsKnownSubject(name));
		parsedSubjects
			.GroupBy(static subject => subject)
			.ToDictionary(static group => group.Key, static group => group.Count())
			.Should()
			.BeEquivalentTo(Catalogue.Subjects.ToDictionary(static subject => subject, static _ => 3));
	}

	[Fact]
	public void catalogue_exclusion_pairs_are_symmetric_and_never_green()
	{
		var pairs = Catalogue.ExclusionPairs.ToArray();

		pairs.Should().NotBeEmpty();
		pairs.Should().OnlyContain(static pair => pair.Severity != Rating.Green);

		foreach (var pair in pairs) {
			Catalogue.Meta(pair.A).Exclusions.Should().ContainSingle(exclusion =>
				exclusion.Other == pair.B && exclusion.Severity == pair.Severity);
			Catalogue.Meta(pair.B).Exclusions.Should().ContainSingle(exclusion =>
				exclusion.Other == pair.A && exclusion.Severity == pair.Severity);
		}
	}

	[Fact]
	public void catalogue_exclusion_pairs_include_the_illustrative_red_clash()
	{
		Catalogue.ExclusionPairs.Should().Contain(pair =>
			(pair.A == Subject.French && pair.B == Subject.German && pair.Severity == Rating.Red)
			|| (pair.A == Subject.German && pair.B == Subject.French && pair.Severity == Rating.Red));
	}

	[Fact]
	public void every_workflow_rule_has_a_name()
	{
		using var doc = WorkflowDocument("eligibility.yaml");

		var ruleNames = doc.RootElement.GetProperty("Rules").EnumerateArray()
			.Select(static rule => rule.GetProperty("RuleName").GetString())
			.ToArray();

		ruleNames.Should().OnlyContain(static name => !string.IsNullOrWhiteSpace(name));
	}

	[Fact]
	public void shipped_workflows_are_yaml_not_json()
	{
		Directory.EnumerateFiles(Harness.WorkflowsDir, "*.json")
			.Where(static file => Path.GetFileName(file) is not WorkflowStore.SchemaFileName)
			.Should()
			.BeEmpty();
	}

	[Fact]
	public void workflow_threshold_comparisons_reference_named_constants_not_bare_numbers()
	{
		var expressions =
			from file in WorkflowFiles()
			where Path.GetFileName(file) is not "trivial.yaml"
			from expression in ExpressionsIn(WorkflowDocument(file).RootElement)
			select (File: Path.GetFileName(file), Expression: expression);

		expressions.Should().OnlyContain(
			expression => !BareNumericComparison().IsMatch(expression.Expression),
			"rule thresholds must reference Thresholds/ALevelGrade constants rather than bare numeric literals");
	}

	[Property(Arbitrary = new[] { typeof(StudentArbitraries) }, MaxTest = 250)]
	public async Task<bool> random_valid_students_never_throw_and_preserve_phase_nine_invariants(StudentInput student)
	{
		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale).Should().BeEmpty();

		var explained = await engine.ExplainAsync(student);

		explained.Explanations.Should().HaveCount(Catalogue.Subjects.Count);
		explained.Explanations.Select(static explanation => explanation.Subject)
			.Should()
			.BeEquivalentTo(Catalogue.Subjects);

		explained.Summary.ProjectedTariff.Should().BeInRange(0, MaxProjectedTariff);

		if (!explained.Eligible) {
			return true;
		}

		// No green-count ceiling: the green cap is an optional feature, disabled in the shipped config, so
		// every legitimate green stays green. The exclusion-pair invariant below is what still bounds greens.
		var greenSubjects = explained.Explanations
			.Where(static explanation => explanation.Rating == Rating.Green)
			.Select(static explanation => explanation.Subject)
			.ToHashSet();

		foreach (var (a, b, _) in Catalogue.ExclusionPairs) {
			var bothGreen = greenSubjects.Contains(a) && greenSubjects.Contains(b);
			bothGreen.Should().BeFalse($"excluding pair {a}/{b} cannot both survive green");
		}

		foreach (var explanation in explained.Explanations) {
			((int)explanation.Rating).Should().BeGreaterThanOrEqualTo(
				(int)explanation.BaseRating,
				$"{explanation.Subject} must not be upgraded by constraints");
		}

		foreach (var explanation in explained.Explanations.Where(static explanation =>
					 explanation.BaseRating != Rating.Red && explanation.Rating == Rating.Red)) {
			explanation.Overrides.Should().Contain(
				static override_ => override_.To == Rating.Red,
				"a downgrade to red must be explained by at least one red adjustment");
		}

		return true;
	}

	private static bool IsKnownSubject(string name) => Subject.TryParse(name, out var subject) && Catalogue.Subjects.Contains(subject);

	[GeneratedRegex(@">=\s*\d")]
	private static partial Regex BareNumericComparison();

	private static JsonDocument WorkflowDocument(string fileName) =>
		JsonDocument.Parse(WorkflowStore.NormalizeWorkflowDocument(
			Path.Combine(Harness.WorkflowsDir, fileName),
			File.ReadAllText(Path.Combine(Harness.WorkflowsDir, fileName))));

	private static IEnumerable<string> WorkflowFiles() =>
		Directory.EnumerateFiles(Harness.WorkflowsDir)
			.Where(static file => Path.GetFileName(file) is not WorkflowStore.SchemaFileName
								  && Path.GetExtension(file) is ".yaml" or ".yml");

	private static IEnumerable<string> ExpressionsIn(JsonElement element)
	{
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject()) {
					if (property.NameEquals("Expression") && property.Value.GetString() is { } expression) {
						yield return expression;
					}

					foreach (var child in ExpressionsIn(property.Value)) {
						yield return child;
					}
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray()) {
					foreach (var child in ExpressionsIn(item)) {
						yield return child;
					}
				}

				break;
		}
	}

	public static class StudentArbitraries
	{
		private static readonly string[] GcseKeys = [.. GcseSubjects.Known.OrderBy(static key => key, StringComparer.Ordinal)];

		private static readonly string[] Hobbies = [
			"plays_piano",
			"plays_guitar",
			"plays_violin",
			"gaming",
			"football",
			"reading",
			"chess",
			"art_club",
		];

		public static Arbitrary<StudentInput> StudentInput() =>
			Arb.From(
				from id in Gen.Choose(1, int.MaxValue).Select(static value => $"S-{value}")
				from gcseKeys in Gen.SubListOf(GcseKeys)
				from grades in Gen.Choose(Thresholds.MinGcseGrade, Thresholds.MaxGcseGrade).ListOf(gcseKeys.Count)
				from hobbies in Gen.SubListOf(Hobbies)
				from birthYear in Gen.Choose(1990, 2010)
				from birthMonth in Gen.Choose(1, 12)
				from birthDay in Gen.Choose(1, 28)
				select new StudentInput(
					id,
					gcseKeys.Zip(grades, static (subject, grade) => new KeyValuePair<string, int>(subject, grade))
						.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
					hobbies) { DateOfBirth = new DateOnly(birthYear, birthMonth, birthDay) });
	}
}
