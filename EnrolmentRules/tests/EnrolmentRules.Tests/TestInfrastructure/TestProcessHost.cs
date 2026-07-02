namespace EnrolmentRules.Tests.TestInfrastructure;

internal static class TestProcessHost
{
	private static readonly TimeSpan RunnerTimeout = TimeSpan.FromSeconds(5);

	public static Task<ProcessResult> RunAsync(string mode, string? argument = null, TimeSpan? timeout = null)
	{
		var arguments = new List<string> { AssemblyPath(), mode };
		if (argument is not null) {
			arguments.Add(argument);
		}

		return TestProcessRunner.RunAsync("dotnet", arguments, Harness.RepoRoot, timeout ?? RunnerTimeout);
	}

	private static string AssemblyPath() =>
		Path.Combine(
			Harness.RepoRoot,
			"tests",
			"EnrolmentRules.TestProcessHost",
			"bin",
			"Debug",
			"net10.0",
			"EnrolmentRules.TestProcessHost.dll");
}
