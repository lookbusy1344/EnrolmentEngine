namespace EnrolmentRules.Tests.TestInfrastructure;

using System.Diagnostics;
using System.Text;

internal static class TestProcessRunner
{
	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

	public static async Task<ProcessResult> RunAsync(
		string fileName,
		IEnumerable<string> arguments,
		string workingDirectory,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		// Materialise once: the sequence is walked here and again when composing the timeout
		// diagnostic, and a single-pass enumerable would otherwise vanish from the message.
		var argumentList = arguments as IReadOnlyList<string> ?? [.. arguments];
		var startInfo = new ProcessStartInfo {
			FileName = fileName,
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		foreach (var argument in argumentList) {
			startInfo.ArgumentList.Add(argument);
		}

		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
		var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
		var effectiveTimeout = timeout ?? DefaultTimeout;
		using var timeoutSource = new CancellationTokenSource(effectiveTimeout);
		using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

		try {
			await process.WaitForExitAsync(linkedSource.Token);
			await Task.WhenAll(stdoutTask, stderrTask);
			return new(process.ExitCode, stdoutTask.Result, stderrTask.Result);
		}
		catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested) {
			throw await CreateTimeoutExceptionAsync(process, fileName, argumentList, effectiveTimeout, stdoutTask, stderrTask, exception);
		}
	}

	private static async Task<TimeoutException> CreateTimeoutExceptionAsync(
		Process process,
		string fileName,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		Task<string> stdoutTask,
		Task<string> stderrTask,
		OperationCanceledException timeoutException)
	{
		try {
			TryKill(process);
			using var cleanupSource = new CancellationTokenSource(CleanupTimeout);
			await process.WaitForExitAsync(cleanupSource.Token);
		}
		catch {
			// The timeout is the primary failure; a kill/wait fault during cleanup must not
			// mask it. Swallow it here so the diagnostic below still reports the timeout.
		}

#pragma warning disable VSTHRD003 // Draining the process-owned stream-read tasks is intentional here.
		var stdout = await DrainAsync(stdoutTask);
		var stderr = await DrainAsync(stderrTask);
#pragma warning restore VSTHRD003
		var message = BuildTimeoutMessage(fileName, arguments, timeout, stdout, stderr);
		return new(message, timeoutException);
	}

	private static async Task<string> DrainAsync(Task<string> outputTask)
	{
#pragma warning disable VSTHRD003 // Draining the process-owned stream-read task is intentional here.
		try {
			return await outputTask;
		}
		catch (OperationCanceledException) {
			return string.Empty;
		}
#pragma warning restore VSTHRD003
	}

	private static void TryKill(Process process)
	{
		try {
			if (!process.HasExited) {
				process.Kill(true);
			}
		}
		catch (InvalidOperationException) {
		}
	}

	private static string BuildTimeoutMessage(
		string fileName,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		string stdout,
		string stderr)
	{
		var builder = new StringBuilder();
		_ = builder.Append("Process timed out after ")
			.Append(timeout)
			.Append(": ")
			.Append(fileName);
		foreach (var argument in arguments) {
			_ = builder.Append(' ').Append(argument);
		}

		_ = builder.AppendLine()
			.AppendLine("stdout:")
			.AppendLine(stdout)
			.AppendLine("stderr:")
			.Append(stderr);
		return builder.ToString();
	}
}

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
