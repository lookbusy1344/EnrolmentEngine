using System.Diagnostics;
using System.Globalization;

internal static class Program
{
	private const int ConcurrentLineCount = 128;
	private const int LargePayloadBytes = 256 * 1024;
	private const int NonZeroExitCode = 23;
	private static readonly TimeSpan ChildSleepDuration = TimeSpan.FromSeconds(60);

	private static string AssemblyPath =>
		Path.Combine(AppContext.BaseDirectory, "EnrolmentRules.TestProcessHost.dll");

	public static Task<int> Main(string[] args) =>
		args switch {
			["concurrent"] => ConcurrentAsync(),
			["large-output"] => LargeOutputAsync(),
			["non-zero"] => NonZeroAsync(),
			["timeout-tree", var pidFilePath] => TimeoutTreeAsync(pidFilePath),
			["sleep-child", var pidFilePath] => SleepChildAsync(pidFilePath),
			_ => throw new InvalidOperationException($"Unknown mode: {string.Join(" ", args)}"),
		};

	private static async Task<int> ConcurrentAsync()
	{
		var stdout = Task.Run(async () => {
			for (var index = 0; index < ConcurrentLineCount; index++) {
				await Console.Out.WriteLineAsync($"stdout:{index}");
			}
		});
		var stderr = Task.Run(async () => {
			for (var index = 0; index < ConcurrentLineCount; index++) {
				await Console.Error.WriteLineAsync($"stderr:{index}");
			}
		});

		await Task.WhenAll(stdout, stderr);
		return 0;
	}

	private static async Task<int> LargeOutputAsync()
	{
		var payload = new string('x', LargePayloadBytes);
		await Console.Out.WriteAsync(payload);
		await Console.Out.WriteLineAsync();
		await Console.Error.WriteLineAsync("stderr:complete");
		return 0;
	}

	private static async Task<int> NonZeroAsync()
	{
		await Console.Out.WriteLineAsync("stdout:before-exit");
		await Console.Error.WriteLineAsync("stderr:before-exit");
		return NonZeroExitCode;
	}

	private static async Task<int> TimeoutTreeAsync(string pidFilePath)
	{
		var startInfo = new ProcessStartInfo {
			FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path."),
			RedirectStandardOutput = false,
			RedirectStandardError = false,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		startInfo.ArgumentList.Add(AssemblyPath);
		startInfo.ArgumentList.Add("sleep-child");
		startInfo.ArgumentList.Add(pidFilePath);
		var child = Process.Start(startInfo);

		if (child is null) {
			throw new InvalidOperationException("Failed to start child process.");
		}

		File.WriteAllText(pidFilePath, child.Id.ToString(CultureInfo.InvariantCulture));
		await Console.Out.WriteLineAsync("parent:waiting");
		child.WaitForExit();
		return child.ExitCode;
	}

	private static async Task<int> SleepChildAsync(string _)
	{
		await Console.Out.WriteLineAsync("child:started");
		await Task.Delay(ChildSleepDuration);
		return 0;
	}
}
