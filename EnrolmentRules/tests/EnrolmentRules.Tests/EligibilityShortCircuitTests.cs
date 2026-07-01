namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using Prediction;
using RulesEngine.Interfaces;
using RulesEngine.Models;

/// <summary>
///     Phase 4 — the eligibility short-circuit as host-code gating (§1.3 consequence). An ineligible
///     student is red in <em>every</em> subject with the gate reason, and the per-subject workflow is
///     never run; an eligible student flows through to the Phase 3 ratings. The "every subject" set is
///     data-driven from <see cref="Subject" />, so adding a subject can't silently skip the gate.
/// </summary>
public sealed class EligibilityShortCircuitTests
{
	// Only Maths present: English absent (first gate failure) and the pass-count fails too — ineligible.
	private static GcseResult[] IneligibleGcses() => [new("maths", Harness.Thresholds.PassGrade)];

	// A uniform top student clears the gate (English + Maths + ≥ MinPasses passes).
	private static StudentInput EligibleStudent() =>
		new("S-OK", new Dictionary<string, int> {
			["english_language"] = 9,
			["maths"] = 9,
			["physics"] = 9,
			["chemistry"] = 9,
			["biology"] = 9,
			["english_literature"] = 9,
			["french"] = 9,
			["german"] = 9,
			["physical_education"] = 9,
			["computer_studies"] = 9,
			["history"] = 9,
			["music"] = 9,
			["art"] = 9,
		}, []);

	private static StudentProfile ProfileFor(IReadOnlyList<GcseResult> gcses) =>
		GradePredictor.Predict(
			new("S", gcses.ToDictionary(g => g.Subject, g => g.Grade), []),
			gcses,
			Harness.AsOf,
			Harness.Catalogue,
			Harness.Scale);

	[Fact]
	public async Task ineligible_student_is_red_in_every_subject_with_the_gate_reason()
	{
		var gcses = IneligibleGcses();

		var evaluator = await Harness.ShippedEvaluatorAsync();
		var ratings = evaluator.Evaluate(ProfileFor(gcses), gcses);

		// Every catalogue subject is present exactly once and red — no subject can dodge the gate.
		ratings.Select(r => r.Subject).Should().BeEquivalentTo(Catalogue.Subjects);
		ratings.Should().HaveCount(Catalogue.Subjects.Count);
		ratings.Should().OnlyContain(r => r.Rating == Rating.Red);

		// The gate reason rides on each red rating; English is the first failed requirement here.
		ratings.Should().OnlyContain(r => r.Reason.Contains("English", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ineligible_student_never_executes_the_subject_workflow()
	{
		var gcses = IneligibleGcses();
		var spy = new RecordingEngine((await Harness.BuildFromShippedWorkflowsAsync()).Engine);
		var thresholds = PolicyThresholdsStore.LoadAndValidate(Harness.DataDir);

		new RatingEvaluator(spy, thresholds).Evaluate(ProfileFor(gcses), gcses);

		spy.ExecutedWorkflows.Should().Contain(RatingEvaluator.EligibilityWorkflow);
		spy.ExecutedWorkflows.Should().NotContain(RatingEvaluator.SubjectRatingsWorkflow);
	}

	[Fact]
	public async Task eligible_student_runs_the_subject_workflow_and_is_not_all_red()
	{
		var student = EligibleStudent();
		var gcses = student.ToGcseResults();
		var spy = new RecordingEngine((await Harness.BuildFromShippedWorkflowsAsync()).Engine);
		var thresholds = PolicyThresholdsStore.LoadAndValidate(Harness.DataDir);

		var ratings = new RatingEvaluator(spy, thresholds).Evaluate(Harness.Predict(student), gcses);

		spy.ExecutedWorkflows.Should().Contain(RatingEvaluator.SubjectRatingsWorkflow);
		ratings.Should().NotContain(r => r.Rating == Rating.Red);
	}
}

/// <summary>
///     A spying <see cref="IRulesEngine" /> decorator that records the workflow names executed and
///     delegates everything to the wrapped engine — used to prove the eligibility short-circuit never
///     runs the per-subject workflow for an ineligible student.
/// </summary>
internal sealed class RecordingEngine(IRulesEngine inner) : IRulesEngine
{
	public List<string> ExecutedWorkflows { get; } = [];

	public ValueTask<List<RuleResultTree>> ExecuteAllRulesAsync(string workflowName, params RuleParameter[] ruleParams)
	{
		ExecutedWorkflows.Add(workflowName);
		return inner.ExecuteAllRulesAsync(workflowName, ruleParams);
	}

	public ValueTask<List<RuleResultTree>> ExecuteAllRulesAsync(string workflowName, params object[] inputs)
	{
		ExecutedWorkflows.Add(workflowName);
		return inner.ExecuteAllRulesAsync(workflowName, inputs);
	}

	public ValueTask<ActionRuleResult> ExecuteActionWorkflowAsync(string workflowName, string ruleName, RuleParameter[] ruleParameters) =>
		inner.ExecuteActionWorkflowAsync(workflowName, ruleName, ruleParameters);

	public void AddWorkflow(params Workflow[] workflows) => inner.AddWorkflow(workflows);

	public void ClearWorkflows() => inner.ClearWorkflows();

	public void RemoveWorkflow(params string[] workflowNames) => inner.RemoveWorkflow(workflowNames);

	public void AddOrUpdateWorkflow(params Workflow[] workflows) => inner.AddOrUpdateWorkflow(workflows);

	public List<string> GetAllRegisteredWorkflowNames() => inner.GetAllRegisteredWorkflowNames();

	public bool ContainsWorkflow(string workflowName) => inner.ContainsWorkflow(workflowName);
}
