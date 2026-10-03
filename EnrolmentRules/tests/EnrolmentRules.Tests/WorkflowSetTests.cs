namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

public sealed class WorkflowSetTests : IDisposable
{
	private readonly string workflowsDirectory = CopyWorkflows();

	public void Dispose() => Directory.Delete(workflowsDirectory, true);

	[Theory]
	[InlineData(RatingEvaluator.EligibilityWorkflow)]
	[InlineData(RatingEvaluator.SubjectRatingsWorkflow)]
	public void create_and_lint_reject_a_missing_required_workflow(string workflowName)
	{
		File.Delete(Path.Combine(workflowsDirectory, workflowName + ".yaml"));

		var act = CreateEngine;

		act.Should().Throw<WorkflowLintException>().WithMessage($"*{workflowName}*missing*");
		AssertLintError(workflowName, "missing");
	}

	[Fact]
	public void create_rejects_an_empty_workflow_set()
	{
		foreach (var file in Directory.EnumerateFiles(workflowsDirectory, "*.yaml")) {
			File.Delete(file);
		}

		var act = CreateEngine;

		var exception = act.Should().Throw<WorkflowLintException>().Which;
		exception.Findings.Should().HaveCount(2);
		exception.Findings.Should().OnlyContain(static finding => finding.Severity == LintSeverity.Error);
	}

	[Theory]
	[InlineData(RatingEvaluator.EligibilityWorkflow)]
	[InlineData(RatingEvaluator.SubjectRatingsWorkflow)]
	public void create_and_lint_reject_duplicate_workflow_names(string workflowName)
	{
		File.Copy(Path.Combine(workflowsDirectory, workflowName + ".yaml"), Path.Combine(workflowsDirectory, "duplicate.yaml"));

		var act = CreateEngine;

		act.Should().Throw<WorkflowLintException>().WithMessage($"*{workflowName}*duplicate*");
		AssertLintError(workflowName, "duplicate");
	}

	[Fact]
	public void a_renamed_required_workflow_cannot_pass_startup()
	{
		var path = Path.Combine(workflowsDirectory, "eligibility.yaml");
		File.WriteAllText(path, File.ReadAllText(path).Replace("WorkflowName: 'eligibility'", "WorkflowName: 'renamed'", StringComparison.Ordinal));

		var act = CreateEngine;

		act.Should().Throw<WorkflowLintException>().WithMessage("*eligibility*missing*");
	}

	[Theory]
	[InlineData(RatingEvaluator.EligibilityWorkflow)]
	[InlineData(RatingEvaluator.SubjectRatingsWorkflow)]
	public void an_incomplete_reload_preserves_the_current_engine_and_can_be_retried(string workflowName)
	{
		using var factory = EnrolmentEngineFactory.Create(workflowsDirectory, Harness.DataDir, Harness.AsOf);
		var before = factory.Current;
		var path = Path.Combine(workflowsDirectory, workflowName + ".yaml");
		var content = File.ReadAllText(path);
		File.Delete(path);

		var act = () => factory.Reload();

		act.Should().Throw<WorkflowLintException>().WithMessage($"*{workflowName}*missing*");
		factory.Current.Should().BeSameAs(before);
		File.WriteAllText(path, content);
		factory.Reload();
		factory.Current.Should().NotBeSameAs(before);
	}

	private EnrolmentEngine CreateEngine() => EnrolmentEngine.Create(workflowsDirectory, Harness.DataDir, Harness.AsOf);

	private void AssertLintError(string workflowName, string message) =>
		WorkflowLinter.Lint(workflowsDirectory, Harness.Catalogue).Should().ContainSingle(finding =>
			finding.Workflow == workflowName && finding.Severity == LintSeverity.Error
											 && finding.Message.Contains(message, StringComparison.Ordinal));

	private static string CopyWorkflows()
	{
		var destination = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(Harness.WorkflowsDir)) {
			File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
		}

		return destination;
	}
}
