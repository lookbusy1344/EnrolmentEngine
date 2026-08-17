namespace EnrolmentRules.Engine;

using Domain;

/// <summary>Counterfactual guidance surface — expensive; keep off the synchronous hot path.</summary>
public interface IEnrolmentAdvisor
{
	CatalogueData Catalogue { get; }

	QualificationScale Scale { get; }

	/// <summary>Counterfactual guidance, as of the host's bound reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	AdviceResult Advise(StudentInput student, CancellationToken cancellationToken = default);

	/// <summary>Counterfactual guidance as of an explicit reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	AdviceResult Advise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	/// <summary>Produces counterfactual guidance with an explicit unsat GCSE advice scope.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="unsatGcses" /> is not a supported advice scope.</exception>
	AdviceResult Advise(StudentInput student, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default);

	/// <summary>Produces counterfactual guidance with an explicit reference date and unsat GCSE advice scope.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="unsatGcses" /> is not a supported advice scope.</exception>
	AdviceResult Advise(StudentInput student, DateOnly asOf, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default);

	/// <summary>Validate the input and produce counterfactual guidance, as of the host's bound reference date.</summary>
	ValidatedEvaluation<AdviceResult> AdviseValidated(StudentInput student, CancellationToken cancellationToken = default);

	/// <summary>Validate the input and produce counterfactual guidance as of an explicit reference date.</summary>
	ValidatedEvaluation<AdviceResult> AdviseValidated(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	/// <summary>Validates the input and produces counterfactual guidance with an explicit unsat GCSE advice scope.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="unsatGcses" /> is not a supported advice scope.</exception>
	ValidatedEvaluation<AdviceResult> AdviseValidated(StudentInput student, UnsatGcseAdvice unsatGcses,
													  CancellationToken cancellationToken = default);

	/// <summary>Validates the input and produces counterfactual guidance with an explicit reference date and advice scope.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="unsatGcses" /> is not a supported advice scope.</exception>
	ValidatedEvaluation<AdviceResult> AdviseValidated(
		StudentInput student,
		DateOnly asOf,
		UnsatGcseAdvice unsatGcses,
		CancellationToken cancellationToken = default);
}
