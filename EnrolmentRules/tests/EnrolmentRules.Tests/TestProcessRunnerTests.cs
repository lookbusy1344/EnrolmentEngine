namespace EnrolmentRules.Tests;

using System.Diagnostics;
using System.Globalization;
using AwesomeAssertions;
using TestInfrastructure;

public sealed class TestProcessRunnerTests
{
	private static readonly TimeSpan TimeoutUnderTest = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan ChildTerminationWait = TimeSpan.FromSeconds(5);

	[Fact]
	[UsesTestInfrastructure]
	public async Task captures_stdout_and_stderr_when_emitted_concurrently()
	{
		var result = await TestProcessHost.RunAsync("concurrent");

		result.ExitCode.Should().Be(0);
		result.Stdout.Should().Contain("stdout:0").And.Contain("stdout:127");
		result.Stderr.Should().Contain("stderr:0").And.Contain("stderr:127");
	}

	[Fact]
	[UsesTestInfrastructure]
	public async Task captures_output_larger_than_a_pipe_buffer_without_deadlock()
	{
		var result = await TestProcessHost.RunAsync("large-output");

		result.ExitCode.Should().Be(0);
		result.Stdout.Length.Should().BeGreaterThan(256 * 1024);
		result.Stderr.Should().Contain("stderr:complete");
	}

	[Fact]
	[UsesTestInfrastructure]
	public async Task preserves_exit_code_stdout_and_stderr_for_non_zero_exit()
	{
		var result = await TestProcessHost.RunAsync("non-zero");

		result.ExitCode.Should().Be(23);
		result.Stdout.Should().Contain("stdout:before-exit");
		result.Stderr.Should().Contain("stderr:before-exit");
	}

	[Fact]
	[UsesTestInfrastructure]
	public async Task timeout_kills_the_process_tree_and_reports_command_and_captured_output()
	{
		var pidFilePath = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"), "child.pid");
		Directory.CreateDirectory(Path.GetDirectoryName(pidFilePath)!);

		var act = async () => await TestProcessHost.RunAsync("timeout-tree", pidFilePath, TimeoutUnderTest);

		var exception = (await act.Should().ThrowAsync<TimeoutException>()).Which;
		exception.Message.Should().Contain("timeout-tree").And.Contain(TimeoutUnderTest.ToString());
		exception.Message.Should().Contain("stdout:");
		WaitForFile(pidFilePath);
		IsProcessRunning(int.Parse(File.ReadAllText(pidFilePath), CultureInfo.InvariantCulture)).Should().BeFalse();
	}

	private static void WaitForFile(string path)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!File.Exists(path) && stopwatch.Elapsed < ChildTerminationWait) {
			Thread.Sleep(50);
		}

		File.Exists(path).Should().BeTrue();
	}

	private static bool IsProcessRunning(int processId)
	{
		try {
			return !Process.GetProcessById(processId).HasExited;
		}
		catch (ArgumentException) {
			return false;
		}
	}
}
