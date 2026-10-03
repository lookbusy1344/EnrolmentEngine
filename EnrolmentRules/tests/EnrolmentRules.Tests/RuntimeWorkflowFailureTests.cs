namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using RulesEngine.Models;

public sealed class RuntimeWorkflowFailureTests
{
	private const int StudentAgeYears = 16;

	[Theory]
	[InlineData(RatingEvaluator.EligibilityWorkflow, RatingEvaluator.MathsPassRule, "lookup.Grade(\"maths\")")]
	[InlineData(RatingEvaluator.SubjectRatingsWorkflow, "maths:green", "facts.Gcse(\"maths\")")]
	public void validated_evaluation_rejects_student_dependent_expression_failures(
		string workflowName, string ruleName, string gradeExpression)
	{
		var workflows = WorkflowStore.LoadAndValidate(Harness.WorkflowsDir);
		var rule = workflows.Single(workflow => workflow.WorkflowName == workflowName).Rules.Single(rule => rule.RuleName == ruleName);
		rule.Expression += $" && 1 / ({gradeExpression} - {Harness.Thresholds.ExceptionalEntry}) > 0";
		var runtime = WorkflowStore.BuildEngine(workflows);
		WorkflowStore.ProbeCompile(runtime, workflows, Harness.CanonicalProbe());
		var engine = new EnrolmentEngine(runtime, Harness.Thresholds, Harness.Catalogue, Harness.AsOf, Harness.Scale, workflows);

		var act = () => engine.EvaluateValidated(EligibleStudent());

		act.Should().Throw<WorkflowException>().WithMessage($"*{workflowName}*failed evaluation*{ruleName}*");
	}

	[Fact]
	public void evaluation_reports_failures_in_child_results()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.EligibilityWorkflow,
				Rules = [
					new() {
						RuleName = "CompositeRequirement",
						Operator = "Or",
						SuccessEvent = "Composite requirement",
						Rules = [
							new() {
								RuleName = "PassingChild", Expression = "true", RuleExpressionType = RuleExpressionType.LambdaExpression,
							},
							new() {
								RuleName = "FaultingChild", Expression = $"1 / (lookup.Grade(\"maths\") - {Harness.Thresholds.ExceptionalEntry}) > 0", RuleExpressionType = RuleExpressionType.LambdaExpression,
							},
						],
					},
				],
			},
		];
		var runtime = WorkflowStore.BuildEngine(workflows);
		WorkflowStore.ProbeCompile(runtime, workflows, Harness.CanonicalProbe());
		var evaluator = new RatingEvaluator(runtime, Harness.Thresholds);

		var act = () => evaluator.EvaluateEligibility(EligibleStudent().ToGcseResults());

		act.Should().Throw<WorkflowException>().WithMessage("*eligibility*failed evaluation*FaultingChild*");
	}

	private static StudentInput EligibleStudent() => new("runtime-fault", new Dictionary<string, int> {
		["maths"] = Harness.Thresholds.ExceptionalEntry,
		["english_language"] = Thresholds.MaxGcseGrade,
		["physics"] = Thresholds.MaxGcseGrade,
		["chemistry"] = Thresholds.MaxGcseGrade,
		["biology"] = Thresholds.MaxGcseGrade,
	}, []) {
		DateOfBirth = Harness.AsOf.AddYears(-StudentAgeYears),
	};
}
