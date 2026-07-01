namespace EnrolmentRules.Benchmarks;

using BenchmarkDotNet.Attributes;
using Domain;
using Engine;
using RulesEngine.Models;

/// <summary>
///     Phase 9 perf baseline: engine construction versus warm single-student evaluation versus a shared
///     engine over a small batch. The construction benchmark isolates the reusable RulesEngine build;
///     the evaluation benchmarks show the amortised runtime path the CLI and batch mode actually hit.
/// </summary>
[MemoryDiagnoser]
public class EnrolmentBenchmarks
{
	private StudentInput adviseStudent = null!;
	private StudentInput[] batch = [];
	private EnrolmentEngine engine = null!;
	private StudentInput student = null!;
	private IReadOnlyList<Workflow> workflows = [];
	private static string RepoRoot { get; } = FindRepoRoot();
	private static string DataDir => Path.Combine(RepoRoot, "data");
	private static string WorkflowsDir => Path.Combine(RepoRoot, "workflows");
	private static string SchemaPath => Path.Combine(WorkflowsDir, WorkflowStore.SchemaFileName);

	[GlobalSetup]
	public void Setup()
	{
		workflows = WorkflowStore.LoadAndValidate(WorkflowsDir, SchemaPath);
		var rulesEngine = WorkflowStore.BuildEngine(workflows);
		var thresholds = PolicyThresholdsStore.LoadAndValidate(DataDir);
		var catalogue = CatalogueStore.LoadAndValidate(DataDir);
		var scale = QualificationScaleStore.LoadAndValidate(DataDir);
		engine = new(new(rulesEngine, thresholds, catalogue, scale), catalogue, DateOnly.FromDateTime(DateTime.Today));
		student = StrongStudent("S-BENCH-1", "plays_piano");

		// Worst case for the counterfactual advisor: an eligible-but-middling student whose subjects are
		// mostly amber/red, so the advisor runs a grade search per amber/red subject and each search expands
		// many nodes (every node re-runs the whole predict → engine pipeline).
		adviseStudent = new("S-ADVISE", new Dictionary<string, int> {
			["english_language"] = 6,
			["maths"] = 6,
			["physics"] = 6,
			["chemistry"] = 6,
			["biology"] = 6,
			["english_literature"] = 6,
			["french"] = 6,
			["german"] = 6,
			["physical_education"] = 6,
			["computer_studies"] = 6,
			["history"] = 6,
			["music"] = 6,
			["art"] = 6,
		}, []);
		batch = [
			student,
			StrongStudent("S-BENCH-2", "gaming"),
			new("S-BENCH-3", new Dictionary<string, int> {
				["english_language"] = 8,
				["maths"] = 8,
				["physics"] = 7,
				["chemistry"] = 7,
				["biology"] = 7,
				["music"] = 7,
				["art"] = 7,
				["history"] = 7,
			}, []),
		];
	}

	[Benchmark(Baseline = true)]
	public void ConstructEngine()
	{
		var candidate = WorkflowStore.BuildEngine(workflows);
		GC.KeepAlive(candidate);
	}

	[Benchmark]
	public EnrolmentResult EvaluateSingle() => engine.Evaluate(student);

	[Benchmark]
	public EnrolmentResult[] EvaluateBatch() => [.. batch.Select(student => engine.Evaluate(student))];

	[Benchmark]
	public AdviceResult Advise() => engine.Advise(adviseStudent);

	private static StudentInput StrongStudent(string id, params string[] hobbies) =>
		new(id, new Dictionary<string, int> {
			["english_language"] = 8,
			["maths"] = 8,
			["physics"] = 8,
			["chemistry"] = 8,
			["biology"] = 8,
			["history"] = 8,
			["music"] = 8,
			["art"] = 8,
		}, hobbies);

	private static string FindRepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			if (File.Exists(Path.Combine(dir.FullName, "EnrolmentRules.slnx"))) {
				return dir.FullName;
			}

			dir = dir.Parent;
		}

		throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
	}
}
