namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 1.6 — the gate-clearing search is now a bounded best-first search
///     through the real eligibility pipeline (<see cref="CounterfactualAdvisor" />), so it honours whatever
///     eligibility rules a policy declares, including a top-N GCSE aggregate rule, without the advisor
///     hard-coding their shapes. Builds a small Elite-shaped fixture policy (English/Maths at grade 7, at
///     least eight GCSEs, best-eight total at least 60, top-seven average at least 7.0) to prove the search
///     reaches every rule simultaneously and reports an honest unreachable result when it cannot.
/// </summary>
public sealed class AdviceTopNTests
{
	private const string EligibilityYaml = """
										   WorkflowName: 'eligibility'
										   Rules:
										     - RuleName: 'EnglishLanguagePass'
										       SuccessEvent: 'GCSE English Language at grade 7 or above'
										       Expression: >-
										         lookup.Grade("english_language") >= policy.PassGrade
										     - RuleName: 'MathsPass'
										       SuccessEvent: 'GCSE Maths at grade 7 or above'
										       Expression: >-
										         lookup.Grade("maths") >= policy.PassGrade
										     - RuleName: 'AtLeastEightGcses'
										       SuccessEvent: 'At least eight GCSE results submitted'
										       Expression: >-
										         lookup.Count >= policy.BestGcseCount
										     - RuleName: 'BestEightTotal'
										       SuccessEvent: 'Best eight GCSE total at or above the minimum'
										       Expression: >-
										         lookup.BestTotal(policy.BestGcseCount) >= policy.MinBestGcsePoints
										     - RuleName: 'TopSevenAverage'
										       SuccessEvent: 'Top seven GCSE average at or above the minimum'
										       Expression: >-
										         lookup.BestAverage(policy.TopGcseAverageCount) >= policy.MinTopGcseAverage
										   """;

	private static readonly PolicyThresholds EliteShaped = Harness.Thresholds with {
		PassGrade = 7,
		BestGcseCount = 8,
		MinBestGcsePoints = 60,
		TopGcseAverageCount = 7,
		MinTopGcseAverage = 7.0,
	};

	private static EnrolmentEngine BuildEngine(PolicyThresholds thresholds)
	{
		var fixture = Harness.WriteFixtureWorkflow("eligibility.yaml", EligibilityYaml);
		File.Copy(
			Path.Combine(Harness.WorkflowsDir, "subject-ratings.yaml"),
			Path.Combine(fixture, "subject-ratings.yaml"));

		var workflows = WorkflowStore.LoadAndValidate(fixture, Harness.SchemaPath);
		var rules = WorkflowStore.BuildEngine(workflows);
		WorkflowStore.ProbeCompile(rules, workflows, Harness.CanonicalProbe(thresholds));
		return new(rules, thresholds, Harness.Catalogue, Harness.AsOf, Harness.Scale, workflows);
	}

	[Fact]
	public void search_reaches_english_maths_count_total_and_average_simultaneously()
	{
		// Eight GCSEs (including English/Maths) all at grade 7: English/Maths/count/top-seven-average
		// already clear; only the best-eight total (56 < 60) is short, so the search must specifically
		// exercise the top-N BestTotal rule while leaving the other four satisfied.
		var engine = BuildEngine(EliteShaped);
		var student = new StudentInput("S-ELITE-CLOSE", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 7,
			["physics"] = 7,
			["chemistry"] = 7,
			["biology"] = 7,
			["history"] = 7,
			["art"] = 7,
			["music"] = 7,
		}, []) {
			DateOfBirth = new(2009, 9, 1),
		};

		engine.Explain(student).Eligible.Should().BeFalse();

		var advice = engine.Advise(student);

		advice.Eligible.Should().BeFalse();
		advice.Gate.Should().NotBeNull();
		advice.Gate!.Reachable.Should().BeTrue();
		advice.Gate.Changes.Should().NotBeEmpty();

		var improved = ApplyChanges(student, advice.Gate.Changes);
		engine.Explain(improved).Eligible.Should().BeTrue();
	}

	[Fact]
	public void an_unreachable_gate_reports_unreachable_rather_than_a_partial_bundle()
	{
		// A tight budget makes the multi-rule Elite gate unreachable from a low starting point.
		var tight = EliteShaped with {
			AdviceMaxGradeCost = 1,
			AdviceMaxSubjectsChanged = 1,
		};
		var engine = BuildEngine(tight);
		var student = new StudentInput("S-ELITE-FAR", new Dictionary<string, int> {
			["english_language"] = 4,
			["maths"] = 4,
			["physics"] = 4,
		}, []) {
			DateOfBirth = new(2009, 9, 1),
		};

		var advice = engine.Advise(student);

		advice.Eligible.Should().BeFalse();
		advice.Gate.Should().NotBeNull();
		advice.Gate!.Reachable.Should().BeFalse();
		advice.Gate.Changes.Should().BeEmpty();
	}

	private static StudentInput ApplyChanges(StudentInput student, IEnumerable<GradeChange> changes)
	{
		var gcses = student.Gcses?.ToDictionary(static kv => kv.Key, static kv => kv.Value) ?? [];
		foreach (var change in changes) {
			gcses[change.GcseSubject] = change.To;
		}

		return student with {
			Gcses = EquatableDictionaryFactory.CopyOf(gcses),
		};
	}
}
