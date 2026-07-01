namespace EnrolmentRules.Cli;

internal static class Program
{
	// Thin shim: the runner is driven in-process by tests, so the flag surface is a small hand-rolled
	// pattern match in CliRunner (kept over System.CommandLine to keep that in-process testability and
	// avoid a heavier dependency for a fixed, four-flag CLI).
	public static int Main(string[] args) => CliRunner.Run(args, Console.Out, Console.Error);
}
