namespace EnrolmentRules.Engine;

using Domain;

/// <summary>Counterfactual guidance surface — expensive; keep off the synchronous hot path.</summary>
public interface IEnrolmentAdvisor
{
	CatalogueData Catalogue { get; }

	QualificationScale Scale { get; }

	Task<AdviceResult> AdviseAsync(StudentInput student, CancellationToken cancellationToken = default);

	Task<AdviceResult> AdviseAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	Task<AdviceResult> AdviseAsync(StudentInput student, bool considerUnsatGcses, CancellationToken cancellationToken = default);

	Task<AdviceResult> AdviseAsync(StudentInput student, DateOnly asOf, bool considerUnsatGcses, CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(StudentInput student, CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(StudentInput student, bool considerUnsatGcses,
		CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default);
}
