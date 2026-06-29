namespace EnrolmentRules.Tests;

using Cli;
using Domain;
using Engine;
using FluentAssertions;
using RulesEngine.Models;

/// <summary>
///     Workflow linter tests. These exercise the linter directly over in-memory workflow graphs so the
///     structural checks fail before any student input can trigger them.
/// </summary>
public sealed class WorkflowLinterTests
{
	[Fact]
	public void missing_subject_tier_is_reported()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.SubjectRatingsWorkflow,
				Rules = [
					new() { RuleName = "maths:green", Expression = "facts.Gcse(\"maths\") >= Thresholds.TopEntry" },
					new() { RuleName = "maths:red", Expression = "true" },
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Workflow == RatingEvaluator.SubjectRatingsWorkflow
			&& finding.Rule == "maths:amber"
			&& finding.Severity == LintSeverity.Error
			&& finding.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)
			&& finding.Message.Contains("amber", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void non_trivial_red_expression_is_rejected()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.SubjectRatingsWorkflow,
				Rules = [
					new() { RuleName = "maths:green", Expression = "facts.Gcse(\"maths\") >= Thresholds.TopEntry" },
					new() { RuleName = "maths:amber", Expression = "facts.Gcse(\"maths\") >= Thresholds.StrongEntry" },
					new() { RuleName = "maths:red", Expression = "facts.Predicted(\"maths\") < ALevelGrade.C" },
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "maths:red"
			&& finding.Message.Contains("unconditional true", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void amber_before_green_is_reported_as_unreachable()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.SubjectRatingsWorkflow,
				Rules = [
					new() { RuleName = "maths:amber", Expression = "facts.Gcse(\"maths\") >= Thresholds.StrongEntry" },
					new() { RuleName = "maths:green", Expression = "facts.Gcse(\"maths\") >= Thresholds.TopEntry" },
					new() { RuleName = "maths:red", Expression = "true" },
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "maths"
			&& finding.Message.Contains("ordered green", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void typoed_facts_member_is_reported()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.SubjectRatingsWorkflow,
				Rules = [
					new() { RuleName = "maths:green", Expression = "facts.Prediced(\"maths\") >= ALevelGrade.A" },
					new() { RuleName = "maths:amber", Expression = "facts.Predicted(\"maths\") >= ALevelGrade.B" },
					new() { RuleName = "maths:red", Expression = "true" },
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "maths:green"
			&& finding.Message.Contains("facts.Prediced", StringComparison.Ordinal));
	}

	[Fact]
	public void typoed_gcse_subject_key_is_reported()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.SubjectRatingsWorkflow,
				Rules = [
					new() { RuleName = "maths:green", Expression = "facts.Gcse(\"mathz\") >= Thresholds.TopEntry" },
					new() { RuleName = "maths:amber", Expression = "facts.Gcse(\"maths\") >= Thresholds.StrongEntry" },
					new() { RuleName = "maths:red", Expression = "true" },
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "maths:green"
			&& finding.Severity == LintSeverity.Error
			&& finding.Message.Contains("mathz", StringComparison.Ordinal)
			&& finding.Message.Contains("unknown subject key", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void typoed_predicted_subject_key_is_reported()
	{
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.SubjectRatingsWorkflow,
				Rules = [
					new() { RuleName = "maths:green", Expression = "facts.Predicted(\"further_mathz\") >= ALevelGrade.A" },
					new() { RuleName = "maths:amber", Expression = "facts.Predicted(\"maths\") >= ALevelGrade.B" },
					new() { RuleName = "maths:red", Expression = "true" },
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().ContainSingle(finding =>
			finding.Rule == "maths:green"
			&& finding.Severity == LintSeverity.Error
			&& finding.Message.Contains("further_mathz", StringComparison.Ordinal)
			&& finding.Message.Contains("unknown subject key", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void cross_vocabulary_subject_keys_are_accepted()
	{
		// english_language is a GCSE key but not an A-level Subject; further_maths is the reverse.
		// Each must be valid only for the accessor that reads its vocabulary.
		Workflow[] workflows = [
			new() {
				WorkflowName = RatingEvaluator.SubjectRatingsWorkflow,
				Rules = [
					new() {
						RuleName = "maths:green",
						Expression = "facts.Gcse(\"english_language\") >= Thresholds.TopEntry && facts.Predicted(\"further_maths\") >= ALevelGrade.A",
					},
					new() { RuleName = "maths:amber", Expression = "facts.Gcse(\"maths\") >= Thresholds.StrongEntry" },
					new() { RuleName = "maths:red", Expression = "true" },
				],
			},
		];

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().NotContain(finding => finding.Message.Contains("unknown subject key", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void shipped_workflows_lint_clean()
	{
		var workflows = WorkflowStore.LoadAndValidate(Harness.WorkflowsDir, Harness.SchemaPath);

		var findings = WorkflowLinter.Lint(workflows, Harness.Catalogue);

		findings.Should().BeEmpty();
	}

	[Fact]
	public void custom_subject_workflows_lint_clean_against_their_matching_catalogue()
	{
		var fixture = WriteCustomSubjectFixture();
		try {
			var workflowsDir = Path.Combine(fixture, "workflows");
			var dataDir = Path.Combine(fixture, "data");
			var workflows = WorkflowStore.LoadAndValidate(workflowsDir, Path.Combine(workflowsDir, WorkflowStore.SchemaFileName));
			var catalogue = CatalogueStore.LoadAndValidate(dataDir);

			var findings = WorkflowLinter.Lint(workflows, catalogue);

			findings.Should().BeEmpty();
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public async Task cli_lint_workflows_passes_on_shipped_workflows()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--lint-workflows"], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();
	}

	[Fact]
	public async Task cli_lint_workflows_on_broken_dir_exits_lint_with_findings()
	{
		var brokenDir = CopyShippedWorkflows();
		try {
			var ratingsPath = Path.Combine(brokenDir, "subject-ratings.yaml");
			var corrupted = (await File.ReadAllTextAsync(ratingsPath))
				.Replace("facts.Predicted", "facts.Prediced", StringComparison.Ordinal);
			await File.WriteAllTextAsync(ratingsPath, corrupted);

			using var stdout = new StringWriter();
			using var stderr = new StringWriter();

			var exit = await CliRunner.RunAsync(["--lint-workflows", brokenDir], stdout, stderr);

			exit.Should().Be(CliRunner.ExitLint);
			stdout.ToString().Should().Contain("facts.Prediced");
		}
		finally {
			Directory.Delete(brokenDir, true);
		}
	}

	[Fact]
	public async Task cli_lint_workflows_uses_the_matching_catalogue_for_a_custom_subject_fixture()
	{
		var fixture = WriteCustomSubjectFixture();
		try {
			using var stdout = new StringWriter();
			using var stderr = new StringWriter();

			var exit = await CliRunner.RunAsync(["--lint-workflows", Path.Combine(fixture, "workflows")], stdout, stderr);

			exit.Should().Be(CliRunner.ExitOk);
			stdout.ToString().Should().BeEmpty();
			stderr.ToString().Should().BeEmpty();
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	private static string CopyShippedWorkflows()
	{
		var destination = Path.Combine(Path.GetTempPath(), "lint-fixture-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(Harness.WorkflowsDir)) {
			File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
		}

		return destination;
	}

	private static string WriteCustomSubjectFixture()
	{
		var dir = Path.Combine(Path.GetTempPath(), "lint-custom-subject-" + Guid.NewGuid().ToString("N"));
		var dataDir = Path.Combine(dir, "data");
		var workflowsDir = Path.Combine(dir, "workflows");
		Directory.CreateDirectory(dataDir);
		Directory.CreateDirectory(workflowsDir);

		File.Copy(Path.Combine(Harness.DataDir, CatalogueStore.SchemaFileName), Path.Combine(dataDir, CatalogueStore.SchemaFileName));
		File.Copy(Path.Combine(Harness.WorkflowsDir, WorkflowStore.SchemaFileName), Path.Combine(workflowsDir, WorkflowStore.SchemaFileName));
		File.Copy(Path.Combine(Harness.WorkflowsDir, "eligibility.yaml"), Path.Combine(workflowsDir, "eligibility.yaml"));

		File.WriteAllText(
			Path.Combine(dataDir, CatalogueStore.CatalogueFileName),
			File.ReadAllText(Path.Combine(Harness.DataDir, CatalogueStore.CatalogueFileName))
			+ """

			    - subject: philosophy
			      ucas_weight: 60
			      regression: { slope: 0.90, intercept: -1.00 }
			  """);

		File.WriteAllText(
			Path.Combine(workflowsDir, "subject-ratings.yaml"),
			File.ReadAllText(Path.Combine(Harness.WorkflowsDir, "subject-ratings.yaml"))
			+ """

			    - RuleName: 'philosophy:green'
			      SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
			      Expression: >-
			        facts.Average >= facts.HumanitiesAverageEntry && facts.Predicted("philosophy") >= ALevelGrade.B
			    - RuleName: 'philosophy:amber'
			      SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
			      Expression: >-
			        facts.Average >= facts.HumanitiesAverageEntry && facts.Predicted("philosophy") >= ALevelGrade.C
			    - RuleName: 'philosophy:red'
			      SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
			      Expression: >-
			        true
			  """);

		return dir;
	}
}
