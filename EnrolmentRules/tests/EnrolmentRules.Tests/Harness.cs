namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using Prediction;
using RulesEngine.Interfaces;
using RulesEngine.Models;

/// <summary>
///     Builds the engine over the <em>real</em> shipped workflow files (never test doubles) and provides
///     fixture helpers for the malformed / bad-lambda startup-guard tests.
/// </summary>
internal static class Harness
{
	/// <summary>
	///     The shipped engine, built once and memoised for the whole suite. Loading + JSON-schema
	///     validation + RulesEngine construction + probe-compilation of every lambda (Roslyn) is the
	///     dominant cost; it is identical on every call and the engine is read-only after build, so a
	///     single cached instance is reused across all tests — including the parallel ones. Published
	///     through <see cref="Lazy{T}" /> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication" />
	///     so the build runs exactly once, publishes only a fully probe-compiled engine, and preserves
	///     exception propagation if startup fails. After the build, RulesEngine's compiled-lambda cache is
	///     fully warm, so the concurrent evaluations the parallel suite drives are pure reads.
	/// </summary>
	private static readonly Lazy<Built> shipped = new(BuildShipped, LazyThreadSafetyMode.ExecutionAndPublication);

	public static string RepoRoot { get; } = FindRepoRoot();

	/// <summary>
	///     The fixed reference ("as-of") date the test pipeline derives ages against, so age-dependent
	///     outcomes (and the golden files) stay deterministic regardless of when the suite runs.
	/// </summary>
	public static DateOnly AsOf { get; } = new(2026, 9, 1);

	public static string WorkflowsDir => Path.Combine(RepoRoot, "workflows");

	public static string SchemaPath => Path.Combine(WorkflowsDir, WorkflowStore.SchemaFileName);

	public static string DataDir => Path.Combine(RepoRoot, "data");

	/// <summary>The shipped policy thresholds, loaded once for the host-side tests to pin against.</summary>
	public static PolicyThresholds Thresholds { get; } = PolicyThresholdsStore.LoadAndValidate(DataDir);

	/// <summary>The shipped qualification scale, loaded once for the host-side tests to pin against.</summary>
	public static QualificationScale Scale { get; } = QualificationScaleStore.LoadAndValidate(DataDir);

	/// <summary>The shipped catalogue snapshot, loaded once for the host-side tests to pin against.</summary>
	public static CatalogueData Catalogue { get; } = CatalogueStore.LoadAndValidate(DataDir, Scale);

	/// <summary>Predict a student profile against the shipped catalogue and scale.</summary>
	public static StudentProfile Predict(StudentInput student) =>
		GradePredictor.Predict(student, student.ToGcseResults(), AsOf, Catalogue, Scale);

	/// <summary>Load + validate + probe-compile the shipped workflows and construct the engine over them.</summary>
	public static (IReadOnlyList<Workflow> Workflows, IRulesEngine Engine) BuildFromShippedWorkflows() =>
		(shipped.Value.Workflows, shipped.Value.Engine);

	/// <summary>A <see cref="RatingEvaluator" /> over the shipped workflows for the host-side verdict tests.</summary>
	public static RatingEvaluator ShippedEvaluator() =>
		new(BuildFromShippedWorkflows().Engine, Thresholds, Catalogue, Scale);

	/// <summary>The full <see cref="EnrolmentEngine" /> façade over the shipped workflows (end-to-end tests).</summary>
	public static EnrolmentEngine ShippedEngine() =>
		new(BuildFromShippedWorkflows().Engine, Thresholds, Catalogue, AsOf, Scale);

	/// <summary>
	///     The canonical probe input — the <em>union</em> of every shipped workflow's bound parameters, so
	///     <see cref="WorkflowStore.ProbeCompile" /> can force eager lambda compilation across all of
	///     them in one pass. Grows as new workflows add new inputs.
	/// </summary>
	public static RuleParameter[] CanonicalProbe() => CanonicalProbe(PolicyThresholdsStore.LoadAndValidate(DataDir));

	public static RuleParameter[] CanonicalProbe(PolicyThresholds thresholds)
	{
		var student = WorkflowStore.CanonicalProbeStudent(thresholds);
		var gcses = student.ToGcseResults();
		var lookup = new GcseFacts(gcses);
		var profile = GradePredictor.Predict(student, gcses, AsOf, Catalogue, Scale);

		return [
			.. RatingEvaluator.EligibilityParameters(gcses, lookup, new(thresholds)),
			new("facts", new RatingFacts(profile, lookup, new(thresholds), Catalogue, Scale)),
		];
	}

	/// <summary>Write a single workflow file into a fresh temp directory and return that directory.</summary>
	public static string WriteFixtureWorkflow(string fileName, string content)
	{
		var dir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, fileName), content);
		return dir;
	}

	private static Built BuildShipped()
	{
		var workflows = WorkflowStore.LoadAndValidate(WorkflowsDir, SchemaPath);
		var engine = WorkflowStore.BuildEngine(workflows);
		WorkflowStore.ProbeCompile(engine, workflows, CanonicalProbe());
		return new(workflows, engine);
	}

	private static string FindRepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			if (File.Exists(Path.Combine(dir.FullName, "EnrolmentRules.slnx"))) {
				return dir.FullName;
			}

			dir = dir.Parent;
		}

		throw new InvalidOperationException("Could not locate repo root (EnrolmentRules.slnx) from " + AppContext.BaseDirectory);
	}

	private sealed class Built(IReadOnlyList<Workflow> workflows, IRulesEngine engine)
	{
		public IReadOnlyList<Workflow> Workflows { get; } = workflows;
		public IRulesEngine Engine { get; } = engine;
	}
}

/// <summary>Phase 0 canonical probe input for the trivial workflow. Replaced by the real student probe in Phase 1.</summary>
internal sealed record ProbeInput(double Value);
