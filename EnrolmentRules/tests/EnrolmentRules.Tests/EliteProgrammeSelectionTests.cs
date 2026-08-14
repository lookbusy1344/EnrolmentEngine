namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 3.1 — the final-programme matrix (three or four distinct offered
///     subjects) and policy isolation, driven through the real Elite engine (<see cref="EliteHarness" />)
///     and the shared Standard engine (<see cref="Harness" />).
/// </summary>
public sealed class EliteProgrammeSelectionTests
{
	private static StudentInput EligibleStudent(params Subject[] chosen) =>
		new("S-ELITE", EliteHarness.AllOfferedGcsesAtGrade(9), []) {
			DateOfBirth = new(2009, 9, 1),
			ChosenALevels = [.. chosen],
		};

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public void fewer_than_three_choices_are_incomplete_for_finalisation(int count)
	{
		var engine = EliteHarness.ShippedEngine();
		Subject[] offered = [Subject.Biology, Subject.Chemistry, Subject.History];
		var student = EligibleStudent(offered[..count]);

		var comparisonResult = engine.EvaluateValidated(student);
		var finalResult = engine.ValidateFinalProgramme(student);

		// Incremental editing (the shared basket, comparison view) remains valid at any count.
		comparisonResult.Validation.IsValid.Should().BeTrue();
		// Finalisation enforces the minimum.
		finalResult.Validation.IsValid.Should().BeFalse();
	}

	[Fact]
	public void three_distinct_offered_choices_pass_finalisation()
	{
		var engine = EliteHarness.ShippedEngine();
		var student = EligibleStudent(Subject.Biology, Subject.Chemistry, Subject.History);

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeTrue();
		result.Value!.Subjects.Should().HaveCount(3);
	}

	[Fact]
	public void four_distinct_offered_choices_pass_finalisation()
	{
		var engine = EliteHarness.ShippedEngine();
		var student = EligibleStudent(Subject.Biology, Subject.Chemistry, Subject.History, Subject.Physics);

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeTrue();
		result.Value!.Subjects.Should().HaveCount(4);
	}

	[Fact]
	public void five_choices_fail_finalisation()
	{
		var engine = EliteHarness.ShippedEngine();
		var student = EligibleStudent(
			Subject.Biology, Subject.Chemistry, Subject.History, Subject.Physics, Subject.Psychology);

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeFalse();
		result.Validation.Errors.Should().Contain(e => e.Contains("at most 4", StringComparison.Ordinal));
	}

	[Fact]
	public void a_subject_outside_the_eight_is_not_offered_in_comparison_and_fails_strict_finalisation()
	{
		var registry = new EnrolmentPolicyRegistry(
			[
				new(new("standard"), "Standard", new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir)),
				new(new("elite"), "Elite", new OverlayEnrolmentDataSource(
					new DirectoryDataSource(EliteHarness.WorkflowsDir, EliteHarness.DataDir),
					new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir))),
			],
			new("standard"),
			static () => Harness.AsOf);

		var student = EligibleStudent(Subject.Art);

		var comparison = registry.Compare(new("elite"), student);
		comparison.Validation.IsValid.Should().BeTrue();
		comparison.Value!.ChoiceStatuses.Should().ContainSingle(s => s.Subject == Subject.Art && s.Status == ChoiceStatus.NotOffered);

		var finalResult = EliteHarness.ShippedEngine().ValidateFinalProgramme(student);
		finalResult.Validation.IsValid.Should().BeFalse();
	}

	[Fact]
	public void the_same_shared_basket_produces_different_statuses_under_standard_and_elite()
	{
		var registry = new EnrolmentPolicyRegistry(
			[
				new(new("standard"), "Standard", new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir)),
				new(new("elite"), "Elite", new OverlayEnrolmentDataSource(
					new DirectoryDataSource(EliteHarness.WorkflowsDir, EliteHarness.DataDir),
					new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir))),
			],
			new("standard"),
			static () => Harness.AsOf);

		// A basket of a Standard-only subject (Art) plus an Elite subject (Biology), against grades that
		// clear both policies' entry bars.
		var student = new StudentInput(
			"S-SHARED",
			new Dictionary<string, int> {
				["english_language"] = 9,
				["maths"] = 9,
				["biology"] = 9,
				["art"] = 9,
				["chemistry"] = 9,
				["history"] = 9,
				["physics"] = 9,
				["psychology"] = 9,
			},
			[]) {
			DateOfBirth = new(2009, 9, 1),
			ChosenALevels = [Subject.Art, Subject.Biology],
		};

		var underStandard = registry.Compare(new("standard"), student);
		var underElite = registry.Compare(new("elite"), student);

		underStandard.Value!.ChoiceStatuses.Should().ContainSingle(s => s.Subject == Subject.Art && s.Status == ChoiceStatus.Available);
		underElite.Value!.ChoiceStatuses.Should().ContainSingle(s => s.Subject == Subject.Art && s.Status == ChoiceStatus.NotOffered);
		underElite.Value.ChoiceStatuses.Should().ContainSingle(s => s.Subject == Subject.Biology && s.Status == ChoiceStatus.Available);
	}

	// --- Policy isolation ---

	[Fact]
	public void constructing_and_evaluating_elite_does_not_change_standard_shipped_outputs()
	{
		var eliteEngine = EliteHarness.ShippedEngine();
		var eliteStudent = EligibleStudent(Subject.Biology);
		_ = eliteEngine.Evaluate(eliteStudent);

		var standardEngine = Harness.ShippedEngine();
		var topAllrounder = JsonSerializer.Deserialize(
			File.ReadAllText(Path.Combine(Harness.RepoRoot, "examples", "golden", "top-allrounder.json")),
			EnrolmentJsonContext.Default.StudentDocument)!.Student;

		var result = standardEngine.Evaluate(topAllrounder);
		var expected = File.ReadAllText(Path.Combine(Harness.RepoRoot, "examples", "golden", "top-allrounder.expected.json"));
		var actual = JsonSerializer.Serialize(result, EnrolmentJsonContext.Default.EnrolmentResult);

		actual.ReplaceLineEndings().TrimEnd().Should().Be(expected.ReplaceLineEndings().TrimEnd());
		Catalogue.Default.Subjects.Should().Contain(Subject.Art);
		Harness.Thresholds.PassGrade.Should().Be(4);
	}

	[Fact]
	public void standard_and_elite_evaluate_alternately_without_state_bleed()
	{
		var standardEngine = Harness.ShippedEngine();
		var eliteEngine = EliteHarness.ShippedEngine();
		var eliteStudent = EligibleStudent(Subject.Biology, Subject.Chemistry, Subject.History);
		var standardStudent = new StudentInput("S-STD", new Dictionary<string, int> {
			["maths"] = 8,
		}, []);

		for (var i = 0; i < 5; ++i) {
			var eliteResult = eliteEngine.Evaluate(eliteStudent);
			var standardResult = standardEngine.Evaluate(standardStudent);

			eliteResult.Recommendations.Select(r => r.Subject).Should().NotContain(Subject.Art);
			standardResult.Recommendations.Select(r => r.Subject).Should().Contain(Subject.Art);
		}
	}

	[Fact]
	public void both_startup_probes_compile_independently()
	{
		var act1 = Harness.ShippedEngine;
		var act2 = EliteHarness.ShippedEngine;

		act1.Should().NotThrow();
		act2.Should().NotThrow();
	}

	[Fact]
	public void malformed_elite_yaml_prevents_registry_startup_while_standalone_standard_remains_buildable()
	{
		var brokenWorkflows = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "broken-elite-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(brokenWorkflows);
		File.WriteAllText(Path.Combine(brokenWorkflows, "eligibility.yaml"), "not: [valid, yaml: structure");

		var act = () => new EnrolmentPolicyRegistry(
			[
				new(new("standard"), "Standard", new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir)),
				new(new("elite"), "Elite", new OverlayEnrolmentDataSource(
					new DirectoryDataSource(brokenWorkflows, EliteHarness.DataDir),
					new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir))),
			],
			new("standard"),
			static () => Harness.AsOf);

		act.Should().Throw<EnrolmentPolicyBuildException>().Which.PolicyId.Should().Be(new("elite"));

		var standalone = () => Harness.ShippedEngine();
		standalone.Should().NotThrow();
	}
}
