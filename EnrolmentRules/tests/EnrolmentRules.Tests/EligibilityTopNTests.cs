namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using RulesEngine.Models;

/// <summary>
///     Elite auxiliary policy plan, step 1.3 — generalising the eligibility rule projection: the linter no
///     longer hard-codes exactly EnglishLanguagePass/MathsPass/EnoughPasses, the failure-reason projector
///     falls back to a rule's SuccessEvent for any rule it does not specially recognise, and the narrator
///     can explain the three top-N GCSE aggregate shapes (§1.2's <c>lookup.Count</c>/<c>BestTotal</c>/
///     <c>BestAverage</c>). The Standard workflow/wording stays covered by
///     <see cref="EligibilityGateTests" /> and <see cref="PolicyIsolationTests" />; this file exercises the
///     generalised machinery a top-N auxiliary policy needs.
/// </summary>
public sealed class EligibilityTopNTests
{
	private static readonly PolicyThresholds TopN = Harness.Thresholds with {
		BestGcseCount = 8,
		MinBestGcsePoints = 60,
		TopGcseAverageCount = 7,
		MinTopGcseAverage = 7.0,
	};

	// --- WorkflowLinter: structural invariants, not an exact-name list ---

	[Fact]
	public void an_empty_eligibility_workflow_is_rejected()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow, Rules = [],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Workflow == RatingEvaluator.EligibilityWorkflow
			&& finding.Message.Contains("at least one rule", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void duplicate_eligibility_rule_names_are_rejected()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow,
				Rules = [
					new() {
						RuleName = "BestEightTotal", SuccessEvent = "Best eight GCSE total met", Expression = "true",
					},
					new() {
						RuleName = "BestEightTotal", SuccessEvent = "Best eight GCSE total met", Expression = "true",
					},
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Workflow == RatingEvaluator.EligibilityWorkflow
			&& finding.Rule == "BestEightTotal"
			&& finding.Message.Contains("more than once", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void an_additional_uniquely_named_rule_with_a_success_event_is_accepted()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow,
				Rules = [
					new() {
						RuleName = "EnglishLanguagePass", Expression = "lookup.Grade(\"english_language\") >= policy.PassGrade",
					},
					new() {
						RuleName = "MathsPass", Expression = "lookup.Grade(\"maths\") >= policy.PassGrade",
					},
					new() {
						RuleName = "EnoughPasses",
						LocalParams = [
							new() {
								Name = "passCount", Expression = "gcses.Count(g => g.Grade >= policy.PassGrade)",
							},
						],
						Expression = "passCount >= policy.MinPasses",
					},
					new() {
						RuleName = "BestEightTotal", SuccessEvent = "Best eight GCSE total at or above the minimum", Expression = "lookup.BestTotal(policy.BestGcseCount) >= policy.MinBestGcsePoints",
					},
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().BeEmpty();
	}

	[Fact]
	public void a_new_rule_without_a_success_event_is_rejected()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow,
				Rules = [
					new() {
						RuleName = "BestEightTotal", Expression = "lookup.BestTotal(policy.BestGcseCount) >= policy.MinBestGcsePoints",
					},
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "BestEightTotal" && finding.Message.Contains("SuccessEvent", StringComparison.Ordinal));
	}

	[Fact]
	public void a_specialised_rule_name_with_different_semantics_is_rejected()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow,
				Rules = [
					new() {
						RuleName = "MathsPass", SuccessEvent = "GCSE Mathematics at the exceptional entry grade", Expression = "lookup.Grade(\"maths\") >= policy.ExceptionalEntry",
					},
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "MathsPass" && finding.Message.Contains("specialised failure wording", StringComparison.Ordinal));
	}

	[Fact]
	public void enough_passes_specialised_wording_requires_the_canonical_local_expression()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow,
				Rules = [
					new() {
						RuleName = "EnoughPasses",
						LocalParams = [
							new() {
								Name = "passCount", Expression = "gcses.Count(g => g.Grade >= policy.TopEntry)",
							},
						],
						Expression = "passCount >= policy.MinPasses",
					},
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "EnoughPasses" && finding.Message.Contains("specialised failure wording", StringComparison.Ordinal));
	}

	[Fact]
	public void a_typoed_top_n_member_is_still_caught_by_the_generic_member_check()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow,
				Rules = [
					new() {
						RuleName = "BestEightTotal", SuccessEvent = "Best eight GCSE total at or above the minimum", Expression = "lookup.BestTotall(policy.BestGcseCount) >= policy.MinBestGcsePoints",
					},
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Message.Contains("unknown member", StringComparison.OrdinalIgnoreCase)
			&& finding.Message.Contains("BestTotall", StringComparison.Ordinal));
	}

	// --- ExpressionNarrator: the three top-N shapes ---

	[Fact]
	public void narrator_explains_the_minimum_submitted_gcse_count()
	{
		var narration = ExpressionNarrator.Narrate("lookup.Count >= policy.BestGcseCount", TopN);

		narration.Should().Equal("You have submitted 8 or above GCSE results.");
	}

	[Fact]
	public void narrator_explains_the_best_n_total()
	{
		var narration = ExpressionNarrator.Narrate("lookup.BestTotal(policy.BestGcseCount) >= policy.MinBestGcsePoints", TopN);

		narration.Should().Equal("Your best 8 GCSEs total 60 or above points.");
	}

	[Fact]
	public void narrator_explains_the_top_n_average()
	{
		var narration = ExpressionNarrator.Narrate(
			"lookup.BestAverage(policy.TopGcseAverageCount) >= policy.MinTopGcseAverage", TopN);

		narration.Should().Equal("Your best 7 GCSEs average 7 or above.");
	}

	// --- Full bootstrap: a top-N eligibility rule probe-compiles and runs through the real engine ---

	[Fact]
	public void a_top_n_eligibility_rule_probe_compiles_and_evaluates_through_the_real_engine()
	{
		var fixture = Harness.WriteFixtureWorkflow("eligibility.yaml", """
																	   WorkflowName: 'eligibility'
																	   Rules:
																	     - RuleName: 'BestEightTotal'
																	       SuccessEvent: 'Best eight GCSE total at or above the minimum'
																	       Expression: >-
																	         lookup.BestTotal(policy.BestGcseCount) >= policy.MinBestGcsePoints
																	   """);
		try {
			var workflows = WorkflowStore.LoadAndValidate(fixture, Harness.SchemaPath);
			var engine = WorkflowStore.BuildEngine(workflows);
			WorkflowStore.ProbeCompile(engine, workflows, Harness.CanonicalProbe(TopN));

			var evaluator = new RatingEvaluator(engine, TopN);
			var weakStudent = new GcseResult[] {
				new("maths", 9), new("english_language", 9),
			};
			var strongStudent = Enumerable.Range(0, 8)
										  .Select(static i => new GcseResult($"subject{i}", 8))
										  .ToArray();

			evaluator.EvaluateEligibility(weakStudent).Eligible.Should().BeFalse();
			evaluator.EvaluateEligibility(strongStudent).Eligible.Should().BeTrue();
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}
}
