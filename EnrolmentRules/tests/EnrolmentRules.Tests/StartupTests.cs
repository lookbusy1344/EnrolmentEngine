namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using RulesEngine.Models;

/// <summary>
///     Phase 0 — the solution loads + schema-validates + probe-compiles the workflow files, constructs the
///     engine, and round-trips a trivial workflow. The startup guards (schema + probe) are the
///     Reservation-1 safety net proved here before any real rule exists.
/// </summary>
public sealed class StartupTests
{
	[Fact]
	public async Task workflows_load_and_validate()
	{
		var (workflows, engine) = await Harness.BuildFromShippedWorkflowsAsync();

		workflows.Should().NotBeEmpty();
		engine.Should().NotBeNull();
	}

	[Fact]
	public async Task shipped_engine_construction_probe_compiles_at_startup()
	{
		var engine = await WorkflowStore.LoadValidateBuildAndProbeAsync(Harness.WorkflowsDir, Harness.Catalogue, Harness.SchemaPath);

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
	public async Task bad_lambda_field_is_rejected_at_startup()
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
		var act = async () => await WorkflowStore.ProbeCompileAsync(engine, workflows, probe);

		await act.Should().ThrowAsync<WorkflowProbeException>()
			.WithMessage("*bad-field*");
	}

	[Theory]
	[InlineData(5.0, true)]
	[InlineData(-1.0, false)]
	public async Task trivial_workflow_evaluates(double value, bool expectedSuccess)
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

		await WorkflowStore.ProbeCompileAsync(engine, workflows, new RuleParameter("input1", new ProbeInput(0.0)));

		var results = await engine.ExecuteAllRulesAsync("trivial", probe);

		var rule = results.Should().ContainSingle(r => r.Rule.RuleName == "ValueIsNonNegative").Subject;
		rule.IsSuccess.Should().Be(expectedSuccess);
	}

	[Fact]
	public async Task yaml_workflow_defaults_missing_rule_expression_type_to_lambda_expression()
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

		await WorkflowStore.ProbeCompileAsync(engine, workflows, new RuleParameter("input1", new ProbeInput(0.0)));

		var results = await engine.ExecuteAllRulesAsync("defaulted", new RuleParameter("input1", new ProbeInput(1.0)));

		results.Should().ContainSingle(r => r.Rule.RuleName == "ValueIsNonNegative")
			.Which.IsSuccess.Should().BeTrue();
	}
}
