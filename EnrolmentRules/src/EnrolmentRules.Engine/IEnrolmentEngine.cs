namespace EnrolmentRules.Engine;

using Domain;

/// <summary>
///     The evaluation surface of <see cref="EnrolmentEngine" />, extracted so consumer code can depend on an
///     abstraction and substitute a fake in their own tests. The concrete engine is stateless and reuse-safe,
///     so it is registered as a singleton; this interface carries no lifetime of its own.
/// </summary>
public interface IEnrolmentEngine
{
	/// <summary>The catalogue this engine evaluates against (the same table boundary validation should use).</summary>
	CatalogueData Catalogue { get; }

	/// <summary>The qualification scale this engine evaluates against.</summary>
	QualificationScale Scale { get; }

	/// <summary>The whole-student §1.7 verdict as of the engine's bound reference date.</summary>
	Task<EnrolmentResult> EvaluateAsync(StudentInput student);

	/// <summary>The whole-student §1.7 verdict as of an explicit reference date (per-request hosting).</summary>
	Task<EnrolmentResult> EvaluateAsync(StudentInput student, DateOnly asOf);

	/// <summary>The verdict with per-recommendation provenance attached, as of the bound reference date.</summary>
	Task<ExplainedResult> ExplainAsync(StudentInput student);

	/// <summary>The explained verdict as of an explicit reference date.</summary>
	Task<ExplainedResult> ExplainAsync(StudentInput student, DateOnly asOf);

	/// <summary>Counterfactual guidance over the same pipeline, as of the bound reference date.</summary>
	Task<AdviceResult> AdviseAsync(StudentInput student);

	/// <summary>Counterfactual guidance as of an explicit reference date.</summary>
	Task<AdviceResult> AdviseAsync(StudentInput student, DateOnly asOf);

	/// <summary>
	///     Counterfactual guidance with an explicit override of the loaded
	///     <see cref="PolicyThresholds.AdviceConsidersUnsatGcses" /> diagnostic default, as of the bound date.
	/// </summary>
	Task<AdviceResult> AdviseAsync(StudentInput student, bool considerUnsatGcses);

	/// <summary>Counterfactual guidance with an explicit diagnostic override, as of an explicit reference date.</summary>
	Task<AdviceResult> AdviseAsync(StudentInput student, DateOnly asOf, bool considerUnsatGcses);
}
