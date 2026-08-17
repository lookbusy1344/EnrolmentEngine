namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Cli;
using Domain;
using TestInfrastructure;

/// <summary>
///     Phase 10 — published CLI runtime assets. The deployed executable must carry the shipped
///     workflow YAML and the DfE transition matrix beside itself, rather than depending on the source
///     checkout layout to locate them at runtime.
/// </summary>
public sealed class RuntimeAssetTests
{
	private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

	// Web's publish rebuilds the ClientApp (pnpm install/build) before ASP.NET static-asset discovery
	// unless wwwroot/app/manifest.json is already up to date, so this needs far more headroom than the
	// CLI's plain dotnet publish.
	private static readonly TimeSpan WebPublishTimeout = TimeSpan.FromSeconds(180);

	[Fact]
	[UsesTestInfrastructure]
	public async Task published_cli_contains_its_runtime_assets_and_can_evaluate_outside_the_source_tree()
	{
		var publishDir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "publish-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(publishDir);

		var cliProject = Path.Combine(Harness.RepoRoot, "src", "EnrolmentRules.Cli", "EnrolmentRules.Cli.csproj");
		var publish = await TestProcessRunner.RunAsync(
			"dotnet",
			["publish", cliProject, "-c", "Debug", "--no-restore", "--disable-build-servers", "-o", publishDir],
			Harness.RepoRoot,
			ProcessTimeout);

		publish.ExitCode.Should().Be(0, publish.Stderr);

		File.Exists(Path.Combine(publishDir, "workflows", "eligibility.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "workflows", "subject-ratings.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "workflows", "workflow.schema.json")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "dfe-transition-matrices", "gce-a-level-2019-transition-probabilities.csv")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "catalogue.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "catalogue.schema.json")).Should().BeTrue();

		// The Elite auxiliary policy: only its own workflows/catalogue/thresholds — no copied schemas,
		// qualifications or transition matrix (those stay single-copy above, under the shared data/ tree
		// OverlayEnrolmentDataSource reads from at runtime).
		File.Exists(Path.Combine(publishDir, "policies", "elite", "workflows", "eligibility.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "workflows", "subject-ratings.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "data", "catalogue.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "data", "thresholds.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "workflows", "workflow.schema.json")).Should().BeFalse();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "data", "catalogue.schema.json")).Should().BeFalse();

		var executable = Path.Combine(
			publishDir,
			OperatingSystem.IsWindows() ? "EnrolmentRules.Cli.exe" : "EnrolmentRules.Cli");
		File.Exists(executable).Should().BeTrue();

		var inputPath = Path.Combine(publishDir, "student.json");
		File.WriteAllText(
			inputPath,
			"""
			{"student":{"id":"S-OK","gcses":{"english_language":6,"maths":6,"physics":6,"chemistry":6,"biology":6},"hobbies":[],"date_of_birth":"2009-09-01"}}
			""");

		var run = await TestProcessRunner.RunAsync(executable, ["--json", inputPath], publishDir, ProcessTimeout);

		run.ExitCode.Should().Be(CliRunner.ExitOk, run.Stderr);
		run.Stderr.Should().BeEmpty();
		var result = JsonSerializer.Deserialize(run.Stdout, EnrolmentJsonContext.Default.EnrolmentResult);
		result.Should().NotBeNull();
		result!.Eligible.Should().BeTrue();
	}

	/// <summary>
	///     F5 — the policy-asset item set is declared once (<c>build/EnrolmentRules.PolicyAssets.props</c>)
	///     and both executable hosts import it, so Web's published layout must match CLI's exactly.
	/// </summary>
	[Fact]
	[UsesTestInfrastructure]
	public async Task published_web_contains_the_same_policy_asset_layout_as_the_cli()
	{
		var publishDir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "publish-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(publishDir);

		var webProject = Path.Combine(Harness.RepoRoot, "src", "EnrolmentRules.Web", "EnrolmentRules.Web.csproj");
		var publish = await TestProcessRunner.RunAsync(
			"dotnet",
			["publish", webProject, "-c", "Debug", "--no-restore", "--disable-build-servers", "-o", publishDir],
			Harness.RepoRoot,
			WebPublishTimeout);

		publish.ExitCode.Should().Be(0, publish.Stderr);

		File.Exists(Path.Combine(publishDir, "workflows", "eligibility.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "workflows", "subject-ratings.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "workflows", "workflow.schema.json")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "dfe-transition-matrices", "gce-a-level-2019-transition-probabilities.csv")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "catalogue.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "catalogue.schema.json")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "qualifications.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "thresholds.yaml")).Should().BeTrue();

		File.Exists(Path.Combine(publishDir, "policies", "elite", "workflows", "eligibility.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "workflows", "subject-ratings.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "data", "catalogue.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "data", "thresholds.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "workflows", "workflow.schema.json")).Should().BeFalse();
		File.Exists(Path.Combine(publishDir, "policies", "elite", "data", "catalogue.schema.json")).Should().BeFalse();

		// Web's own static-web-asset layout stays untouched by the shared policy-asset import.
		File.Exists(Path.Combine(publishDir, "wwwroot", "app", "manifest.json")).Should().BeTrue();
	}
}
