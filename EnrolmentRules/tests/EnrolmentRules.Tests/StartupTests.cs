namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using RulesEngine.Interfaces;
using RulesEngine.Models;

/// <summary>
///     Phase 0 — the solution loads + schema-validates + probe-compiles the workflow files, constructs the
///     engine, and round-trips a trivial workflow. The startup guards (schema + probe) are the
///     Reservation-1 safety net proved here before any real rule exists.
/// </summary>
public sealed class StartupTests
{
	[Fact]
	public void workflows_load_and_validate()
	{
		var (workflows, engine) = Harness.BuildFromShippedWorkflows();

		workflows.Should().NotBeEmpty();
		engine.Should().NotBeNull();
	}

	[Fact]
	public void shipped_engine_construction_probe_compiles_at_startup()
	{
		var engine = WorkflowStore.LoadValidateBuildAndProbe(Harness.WorkflowsDir, Harness.Catalogue, Harness.SchemaPath);

		engine.Should().NotBeNull();
	}

	[Fact]
	public void canonical_probe_student_populates_every_known_gcse_subject()
	{
		// The probe must cover every recognised GCSE subject so it forces compilation of all subject rules.
		// Deriving it from GcseSubjects.Known keeps it from drifting against a hand-maintained literal list.
		var student = WorkflowStore.CanonicalProbeStudent(Harness.Thresholds);

		student.Gcses!.Value.Keys.Should().BeEquivalentTo(GcseSubjects.Known);
		student.Gcses!.Value.Values.Should().AllSatisfy(grade => grade.Should().Be(Harness.Thresholds.TopEntry));
	}

	[Fact]
	public void shipped_workflows_contain_only_policy_workflows()
	{
		var workflows = WorkflowStore.LoadAndValidate(Harness.WorkflowsDir, Harness.SchemaPath);

		workflows.Select(static workflow => workflow.WorkflowName)
			.Should()
			.BeEquivalentTo(RatingEvaluator.EligibilityWorkflow, RatingEvaluator.SubjectRatingsWorkflow);
	}

	[Fact]
	public void malformed_workflow_is_rejected_at_startup()
	{
		// Schema-invalid: a rule with neither RuleName nor an Expression/Rules body.
		const string badYaml = """
							   WorkflowName: structurally-broken
							   Rules:
							     - SuccessEvent: missing name and expression
							   """;
		var dir = Harness.WriteFixtureWorkflow("broken.yaml", badYaml);

		var act = () => WorkflowStore.LoadAndValidate(dir, Harness.SchemaPath);

		act.Should().Throw<WorkflowSchemaException>()
			.WithMessage("*broken.yaml*");
	}

	[Fact]
	public void bad_lambda_field_is_rejected_at_startup()
	{
		// Schema-valid, but the lambda references a field the probe input does not have:
		// the schema cannot see this — only probe-evaluation catches it.
		const string badLambda = """
								 WorkflowName: bad-field
								 Rules:
								   - RuleName: ReferencesNonexistentField
								     RuleExpressionType: LambdaExpression
								     Expression: 'input1.NoSuchField >= 0'
								 """;
		var dir = Harness.WriteFixtureWorkflow("bad-field.yaml", badLambda);

		var workflows = WorkflowStore.LoadAndValidate(dir, Harness.SchemaPath); // schema passes
		var engine = WorkflowStore.BuildEngine(workflows);

		var probe = new RuleParameter("input1", new ProbeInput(1.0));
		var act = () => WorkflowStore.ProbeCompile(engine, workflows, probe);

		act.Should().Throw<WorkflowProbeException>()
			.WithMessage("*bad-field*");
	}

	[Fact]
	public void probe_compile_surfaces_the_engine_error_for_a_missing_workflow()
	{
		var (_, engine) = Harness.BuildFromShippedWorkflows();
		Workflow[] missing = [new() { WorkflowName = "missing-workflow" }];

		var act = () => WorkflowStore.ProbeCompile(engine, missing, Harness.CanonicalProbe());

		var exception = act.Should().Throw<WorkflowProbeException>().Which;
		exception.Message.Should().Contain("missing-workflow");
		exception.Message.Should().NotContain("completed asynchronously");
		exception.InnerException.Should().NotBeNull();
		exception.InnerException!.Message.Should().Contain("missing-workflow");
	}

	[Theory]
	[InlineData(5.0, true)]
	[InlineData(-1.0, false)]
	public void trivial_workflow_evaluates(double value, bool expectedSuccess)
	{
		const string trivialYaml = """
								   WorkflowName: trivial
								   Rules:
								     - RuleName: ValueIsNonNegative
								       SuccessEvent: value is non-negative
								       ErrorMessage: value is negative
								       RuleExpressionType: LambdaExpression
								       Expression: 'input1.Value >= 0'
								   """;
		var dir = Harness.WriteFixtureWorkflow("trivial.yaml", trivialYaml);
		var workflows = WorkflowStore.LoadAndValidate(dir, Harness.SchemaPath);
		var engine = WorkflowStore.BuildEngine(workflows);
		var probe = new RuleParameter("input1", new ProbeInput(value));

		WorkflowStore.ProbeCompile(engine, workflows, new RuleParameter("input1", new ProbeInput(0.0)));

		var results = ExecuteAllRules(engine, "trivial", probe);

		var rule = results.Should().ContainSingle(r => r.Rule.RuleName == "ValueIsNonNegative").Subject;
		rule.IsSuccess.Should().Be(expectedSuccess);
	}

	[Fact]
	public void yaml_workflow_defaults_missing_rule_expression_type_to_lambda_expression()
	{
		const string defaultedYaml = """
									 WorkflowName: defaulted
									 Rules:
									   - RuleName: ValueIsNonNegative
									     SuccessEvent: value is non-negative
									     ErrorMessage: value is negative
									     Expression: 'input1.Value >= 0'
									 """;
		var dir = Harness.WriteFixtureWorkflow("defaulted.yaml", defaultedYaml);
		var workflows = WorkflowStore.LoadAndValidate(dir, Harness.SchemaPath);
		var engine = WorkflowStore.BuildEngine(workflows);

		WorkflowStore.ProbeCompile(engine, workflows, new RuleParameter("input1", new ProbeInput(0.0)));

		var results = ExecuteAllRules(engine, "defaulted", new RuleParameter("input1", new ProbeInput(1.0)));

		results.Should().ContainSingle(r => r.Rule.RuleName == "ValueIsNonNegative")
			.Which.IsSuccess.Should().BeTrue();
	}

	private static List<RuleResultTree> ExecuteAllRules(IRulesEngine engine, string workflowName, params RuleParameter[] ruleParams)
	{
		var execution = engine.ExecuteAllRulesAsync(workflowName, ruleParams);
		if (!execution.IsCompletedSuccessfully) {
			throw new InvalidOperationException($"Workflow '{workflowName}' did not complete synchronously.");
		}

#pragma warning disable VSTHRD002
		return execution.Result;
#pragma warning restore VSTHRD002
	}
}
