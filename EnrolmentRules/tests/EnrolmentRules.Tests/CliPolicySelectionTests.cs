namespace EnrolmentRules.Tests;

using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Cli;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 3.3 — <c>--policy &lt;id&gt;</c> as a global CLI option accepted
///     before every mode, normalised out of the argument list before dispatch rather than multiplying the
///     list-pattern arms in <see cref="CliRunner.Run(IReadOnlyList{string}, TextWriter, TextWriter)" /> for
///     every ordering.
/// </summary>
public sealed class CliPolicySelectionTests
{
	private static string WriteTemp(string contents, string extension)
	{
		var path = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N") + extension);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
		return path;
	}

	// Eight GCSEs at grade 8 (English/Maths among them): clears Elite's eligibility gate and every
	// offered subject's amber tier (Further Maths excluded — its chosen-Maths prerequisite is separate).
	private static string EliteEligibleStudentPath(string id) => WriteTemp(
		"""
			{"student":{"id":"ID","gcses":{"english_language":8,"maths":8,"biology":8,"chemistry":8,"history":8,"physics":8,"psychology":8,"french":8},"hobbies":[],"date_of_birth":"2009-09-01"}}
			""".Replace("ID", id, StringComparison.Ordinal),
		".json");

	[Fact]
	public void omitted_policy_matches_the_current_standard_invocation_byte_for_byte()
	{
		var path = WriteTemp("""{"student":{"id":"S-OK","gcses":{"maths":8,"english_language":8,"physics":8,"chemistry":8,"biology":8},"hobbies":[],"date_of_birth":"2009-09-01"}}""", ".json");
		using var withPolicyOut = new StringWriter();
		using var withPolicyErr = new StringWriter();
		using var withoutPolicyOut = new StringWriter();
		using var withoutPolicyErr = new StringWriter();

		var withoutPolicyExit = CliRunner.Run(["--json", path], withoutPolicyOut, withoutPolicyErr);
		var withPolicyExit = CliRunner.Run(["--policy", "standard", "--json", path], withPolicyOut, withPolicyErr);

		withPolicyExit.Should().Be(withoutPolicyExit);
		withPolicyOut.ToString().Should().Be(withoutPolicyOut.ToString());
		withPolicyErr.ToString().Should().Be(withoutPolicyErr.ToString());
	}

	[Fact]
	public void elite_produces_a_distinct_eligibility_and_catalogue_result()
	{
		var path = EliteEligibleStudentPath("S-ELITE");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--policy", "elite", "--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk, stderr.ToString());
		var result = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.EnrolmentResult);
		result.Should().NotBeNull();
		result!.Eligible.Should().BeTrue();
		result.Recommendations.Select(r => r.Subject).Should().NotContain(Subject.Art);
		result.Recommendations.Select(r => r.Subject).Should().Contain(Subject.Biology);
	}

	[Fact]
	public void the_option_works_before_every_mode()
	{
		var path = EliteEligibleStudentPath("S-ELITE-2");

		using (var stdout = new StringWriter())
		using (var stderr = new StringWriter()) {
			CliRunner.Run(["--policy", "elite", "--table", path], stdout, stderr).Should().Be(CliRunner.ExitOk, stderr.ToString());
		}

		using (var stdout = new StringWriter())
		using (var stderr = new StringWriter()) {
			CliRunner.Run(["--policy", "elite", "--explain", path], stdout, stderr).Should().Be(CliRunner.ExitOk, stderr.ToString());
		}

		using (var stdout = new StringWriter())
		using (var stderr = new StringWriter()) {
			CliRunner.Run(["--policy", "elite", path], stdout, stderr).Should().Be(CliRunner.ExitOk, stderr.ToString());
		}

		using (var stdout = new StringWriter())
		using (var stderr = new StringWriter()) {
			CliRunner.Run(["--policy", "standard", "--criteria", "physics"], stdout, stderr).Should().Be(CliRunner.ExitOk, stderr.ToString());
		}

		using (var stdout = new StringWriter())
		using (var stderr = new StringWriter()) {
			CliRunner.Run(["--policy", "elite", "--criteria", "physics"], stdout, stderr).Should().Be(CliRunner.ExitOk, stderr.ToString());
		}
	}

	[Fact]
	public void a_repeated_policy_option_is_rejected()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--policy", "standard", "--policy", "elite", "--criteria", "physics"], stdout, stderr);

		exit.Should().Be(CliRunner.ExitUsage);
		stderr.ToString().Should().Contain("once");
	}

	[Fact]
	public void an_unknown_policy_identifier_is_a_deterministic_usage_error()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--policy", "nonexistent", "--criteria", "physics"], stdout, stderr);

		exit.Should().Be(CliRunner.ExitUsage);
		stderr.ToString().Should().Contain("nonexistent");
		stderr.ToString().Should().Contain("standard");
		stderr.ToString().Should().Contain("elite");
	}

	[Fact]
	public void a_missing_policy_value_is_a_deterministic_usage_error()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--policy"], stdout, stderr);

		exit.Should().Be(CliRunner.ExitUsage);
		stderr.ToString().Should().Contain("--policy");
	}

	[Fact]
	public void lint_workflows_with_policy_elite_lints_the_complete_elite_policy_not_just_a_directory()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--policy", "elite", "--lint-workflows"], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk, stdout.ToString() + stderr.ToString());
	}

	[Fact]
	public void batch_uses_one_selected_immutable_engine_across_all_workers()
	{
		var lines = string.Join(
			'\n',
			Enumerable.Range(0, 5).Select(i => """{"student":{"id":"S-ID","gcses":{"english_language":8,"maths":8,"biology":8,"chemistry":8,"history":8,"physics":8,"psychology":8,"french":8},"hobbies":[],"date_of_birth":"2009-09-01"}}"""
				.Replace("ID", i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)));
		var path = WriteTemp(lines, ".jsonl");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--policy", "elite", "--batch", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk, stderr.ToString());
		var outcomes = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
							 .Select(line => JsonSerializer.Deserialize(line, BatchJsonContext.Default.BatchOutcome)!)
							 .ToArray();
		outcomes.Should().HaveCount(5);
		outcomes.Should().OnlyContain(o => o.Error == null && o.Result!.Eligible);
	}
}
