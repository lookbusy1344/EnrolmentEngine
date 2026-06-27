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

	/// <summary>The shipped catalogue snapshot, loaded once for the host-side tests to pin against.</summary>
	public static CatalogueData Catalogue { get; } = CatalogueStore.LoadAndValidate(DataDir);

	/// <summary>Load + validate + probe-compile the shipped workflows and construct the engine over them.</summary>
	public static async Task<(IReadOnlyList<Workflow> Workflows, IRulesEngine Engine)> BuildFromShippedWorkflowsAsync()
	{
		var workflows = WorkflowStore.LoadAndValidate(WorkflowsDir, SchemaPath);
		var engine = WorkflowStore.BuildEngine(workflows);
		await WorkflowStore.ProbeCompileAsync(engine, workflows, CanonicalProbe());
		return (workflows, engine);
	}

	/// <summary>A <see cref="RatingEvaluator" /> over the shipped workflows for the host-side verdict tests.</summary>
	public static async Task<RatingEvaluator> ShippedEvaluatorAsync() =>
		new((await BuildFromShippedWorkflowsAsync()).Engine, Thresholds, Catalogue);

	/// <summary>The full <see cref="EnrolmentEngine" /> façade over the shipped workflows (end-to-end tests).</summary>
	public static async Task<EnrolmentEngine> ShippedEngineAsync() =>
		new((await BuildFromShippedWorkflowsAsync()).Engine, Thresholds, Catalogue, AsOf);

	/// <summary>
	///     The canonical probe input — the <em>union</em> of every shipped workflow's bound parameters, so
	///     <see cref="WorkflowStore.ProbeCompileAsync" /> can force eager lambda compilation across all of
	///     them in one pass. Grows as new workflows add new inputs.
	/// </summary>
	public static RuleParameter[] CanonicalProbe() => CanonicalProbe(PolicyThresholdsStore.LoadAndValidate(DataDir));

	public static RuleParameter[] CanonicalProbe(PolicyThresholds thresholds)
	{
		var student = WorkflowStore.CanonicalProbeStudent(thresholds);
		var gcses = student.ToGcseResults();
		var profile = GradePredictor.Predict(student, gcses, AsOf, Catalogue);

		return [
			.. RatingEvaluator.EligibilityParameters(gcses, thresholds),
			new("facts", new RatingFacts(profile, gcses, new(thresholds), Catalogue, QualificationScale.Current)),
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
}

/// <summary>Phase 0 canonical probe input for the trivial workflow. Replaced by the real student probe in Phase 1.</summary>
internal sealed record ProbeInput(double Value);
