namespace EnrolmentRules.Benchmarks;

using BenchmarkDotNet.Running;

/// <summary>Benchmark entry point.</summary>
public static class Program
{
	public static void Main(string[] args) =>
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
