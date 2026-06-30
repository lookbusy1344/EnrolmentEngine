namespace EnrolmentRules.Tests;

using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using Cli;
using Domain;

/// <summary>
///     Phase 10 — published CLI runtime assets. The deployed executable must carry the shipped
///     workflow YAML and the DfE transition matrix beside itself, rather than depending on the source
///     checkout layout to locate them at runtime.
/// </summary>
public sealed class RuntimeAssetTests
{
	[Fact]
	public async Task published_cli_contains_its_runtime_assets_and_can_evaluate_outside_the_source_tree()
	{
		var publishDir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "publish-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(publishDir);

		var cliProject = Path.Combine(Harness.RepoRoot, "src", "EnrolmentRules.Cli", "EnrolmentRules.Cli.csproj");
		var publish = await RunProcessAsync(
			"dotnet",
			["publish", cliProject, "-c", "Debug", "--no-restore", "-o", publishDir],
			Harness.RepoRoot);

		publish.ExitCode.Should().Be(0, publish.Stderr);

		File.Exists(Path.Combine(publishDir, "workflows", "eligibility.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "workflows", "subject-ratings.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "workflows", "workflow.schema.json")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "dfe-transition-matrices", "gce-a-level-2019-transition-probabilities.csv")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "catalogue.yaml")).Should().BeTrue();
		File.Exists(Path.Combine(publishDir, "data", "catalogue.schema.json")).Should().BeTrue();

		var executable = Path.Combine(
			publishDir,
			OperatingSystem.IsWindows() ? "EnrolmentRules.Cli.exe" : "EnrolmentRules.Cli");
		File.Exists(executable).Should().BeTrue();

		var inputPath = Path.Combine(publishDir, "student.json");
		await File.WriteAllTextAsync(
			inputPath,
			"""
			{"student":{"id":"S-OK","gcses":{"english_language":6,"maths":6,"physics":6,"chemistry":6,"biology":6},"hobbies":[],"date_of_birth":"2009-09-01"}}
			""");

		var run = await RunProcessAsync(executable, ["--json", inputPath], publishDir);

		run.ExitCode.Should().Be(CliRunner.ExitOk, run.Stderr);
		run.Stderr.Should().BeEmpty();
		var result = JsonSerializer.Deserialize(run.Stdout, EnrolmentJsonContext.Default.EnrolmentResult);
		result.Should().NotBeNull();
		result!.Eligible.Should().BeTrue();
	}

	private static async Task<ProcessResult> RunProcessAsync(
		string fileName,
		IEnumerable<string> arguments,
		string workingDirectory,
		TimeSpan? timeout = null)
	{
		var startInfo = new ProcessStartInfo {
			FileName = fileName,
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (var argument in arguments) {
			startInfo.ArgumentList.Add(argument);
		}

		using var process = Process.Start(startInfo);
		process.Should().NotBeNull();

		timeout ??= TimeSpan.FromSeconds(30);
		var outputTask = process!.StandardOutput.ReadToEndAsync();
		var errorTask = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync().WaitAsync(timeout.Value);

		return new(process.ExitCode, await outputTask, await errorTask);
	}

	private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
