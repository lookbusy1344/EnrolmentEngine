namespace EnrolmentRules.Engine;

using Domain;

/// <summary>Counterfactual guidance surface — expensive; keep off the synchronous hot path.</summary>
public interface IEnrolmentAdvisor
{
	CatalogueData Catalogue { get; }

	QualificationScale Scale { get; }

	AdviceResult Advise(StudentInput student, CancellationToken cancellationToken = default);

	AdviceResult Advise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	AdviceResult Advise(StudentInput student, bool considerUnsatGcses, CancellationToken cancellationToken = default);

	AdviceResult Advise(StudentInput student, DateOnly asOf, bool considerUnsatGcses, CancellationToken cancellationToken = default);

	ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, CancellationToken cancellationToken = default);

	ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, bool considerUnsatGcses,
		CancellationToken cancellationToken = default);

	ValidatedEvaluation<AdviceResult> TryAdvise(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default);
}
