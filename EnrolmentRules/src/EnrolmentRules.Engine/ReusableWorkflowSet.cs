namespace EnrolmentRules.Engine.Authoring;

using Domain;
using RulesEngine.Models;

/// <summary>
///     A load-once, build-many handle over the schema-validated workflows. Lets a caller isolate the
///     (repeatable) engine-build cost from the one-off load — a construction benchmark, say — without naming
///     the untyped RulesEngine <see cref="Workflow" /> type, keeping that detail inside the engine boundary.
/// </summary>
internal sealed class ReusableWorkflowSet
{
	private readonly IReadOnlyList<Workflow> workflows;

	internal ReusableWorkflowSet(IReadOnlyList<Workflow> workflows) => this.workflows = workflows;

	/// <summary>Build the Engine-owned façade used by the evaluation benchmarks.</summary>
	internal EnrolmentEngine BuildEnrolmentEngine(
		PolicyThresholds thresholds,
		CatalogueData catalogue,
		QualificationScale scale,
		DateOnly asOf)
	{
		var evaluator = new RatingEvaluator(WorkflowStore.BuildEngine(workflows), thresholds, catalogue, scale);
		return new(evaluator, catalogue, asOf, workflows: workflows);
	}

	/// <summary>
	///     Construct the underlying runtime while exposing it only as an opaque object. The benchmark needs to
	///     retain the result, not invoke the vendor API; keeping the static return type opaque prevents its assembly
	///     from acquiring a RulesEngine metadata reference.
	/// </summary>
	internal object BuildEngineForBenchmark() => WorkflowStore.BuildEngine(workflows);
}
