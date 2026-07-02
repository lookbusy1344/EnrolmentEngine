namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using RulesEngine.Interfaces;
using RulesEngine.Models;

public sealed class RatingEvaluatorTests
{
	[Fact]
	public void evaluate_eligibility_reuses_the_evaluator_policy_facts()
	{
		var engine = new CapturingEngine();
		var evaluator = new RatingEvaluator(engine, Harness.Thresholds);

		_ = evaluator.EvaluateEligibility([]);
		_ = evaluator.EvaluateEligibility([]);

		engine.PolicyFacts.Should().HaveCount(2);
		engine.PolicyFacts[1].Should().BeSameAs(engine.PolicyFacts[0]);
	}

	[Fact]
	public void evaluate_eligibility_surfaces_the_rules_engine_failure_instead_of_the_sync_guard_message()
	{
		var evaluator = new RatingEvaluator(new FaultingEngine(), Harness.Thresholds);

		var act = () => evaluator.EvaluateEligibility([]);

		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("workflow not found");
		exception.Message.Should().NotContain("completed asynchronously");
	}

	private sealed class FaultingEngine : IRulesEngine
	{
		public ValueTask<List<RuleResultTree>> ExecuteAllRulesAsync(string workflowName, params RuleParameter[] ruleParams) =>
			ValueTask.FromException<List<RuleResultTree>>(new InvalidOperationException($"workflow not found: {workflowName}"));

		public ValueTask<List<RuleResultTree>> ExecuteAllRulesAsync(string workflowName, params object[] inputs) =>
			ValueTask.FromException<List<RuleResultTree>>(new InvalidOperationException($"workflow not found: {workflowName}"));

		public ValueTask<ActionRuleResult> ExecuteActionWorkflowAsync(string workflowName, string ruleName, RuleParameter[] ruleParameters) =>
			ValueTask.FromException<ActionRuleResult>(new NotImplementedException());

		public void AddWorkflow(params Workflow[] workflows) => throw new NotImplementedException();

		public void ClearWorkflows() => throw new NotImplementedException();

		public void RemoveWorkflow(params string[] workflowNames) => throw new NotImplementedException();

		public void AddOrUpdateWorkflow(params Workflow[] workflows) => throw new NotImplementedException();

		public List<string> GetAllRegisteredWorkflowNames() => [];

		public bool ContainsWorkflow(string workflowName) => false;
	}

	private sealed class CapturingEngine : IRulesEngine
	{
		public List<object> PolicyFacts { get; } = [];

		public ValueTask<List<RuleResultTree>> ExecuteAllRulesAsync(string workflowName, params RuleParameter[] ruleParams)
		{
			PolicyFacts.Add(ruleParams.Single(parameter => parameter.Name == "policy").Value);
			return ValueTask.FromResult<List<RuleResultTree>>([]);
		}

		public ValueTask<List<RuleResultTree>> ExecuteAllRulesAsync(string workflowName, params object[] inputs) =>
			ValueTask.FromException<List<RuleResultTree>>(new NotImplementedException());

		public ValueTask<ActionRuleResult> ExecuteActionWorkflowAsync(string workflowName, string ruleName, RuleParameter[] ruleParameters) =>
			ValueTask.FromException<ActionRuleResult>(new NotImplementedException());

		public void AddWorkflow(params Workflow[] workflows) => throw new NotImplementedException();

		public void ClearWorkflows() => throw new NotImplementedException();

		public void RemoveWorkflow(params string[] workflowNames) => throw new NotImplementedException();

		public void AddOrUpdateWorkflow(params Workflow[] workflows) => throw new NotImplementedException();

		public List<string> GetAllRegisteredWorkflowNames() => [];

		public bool ContainsWorkflow(string workflowName) => false;
	}
}
