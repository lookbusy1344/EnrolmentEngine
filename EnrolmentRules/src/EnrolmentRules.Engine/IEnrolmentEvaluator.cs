namespace EnrolmentRules.Engine;

using Domain;

/// <summary>Evaluate and explain student verdicts without the counterfactual advisor surface.</summary>
public interface IEnrolmentEvaluator
{
	CatalogueData Catalogue { get; }

	QualificationScale Scale { get; }

	PolicyThresholds Thresholds { get; }

	/// <summary>The whole-student verdict, as of the host's bound reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	EnrolmentResult Evaluate(StudentInput student, CancellationToken cancellationToken = default);

	/// <summary>The whole-student verdict as of an explicit reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	EnrolmentResult Evaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	/// <summary>The same verdict with per-recommendation provenance attached.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	ExplainedResult Explain(StudentInput student, CancellationToken cancellationToken = default);

	/// <summary>The explained verdict as of an explicit reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	ExplainedResult Explain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	/// <summary>Validate the input and return the whole-student verdict, as of the host's bound reference date.</summary>
	ValidatedEvaluation<EnrolmentResult> EvaluateValidated(StudentInput student, CancellationToken cancellationToken = default);

	/// <summary>Validate the input and return the whole-student verdict as of an explicit reference date.</summary>
	ValidatedEvaluation<EnrolmentResult> EvaluateValidated(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	/// <summary>Validate the input and return the explained verdict, as of the host's bound reference date.</summary>
	ValidatedEvaluation<ExplainedResult> ExplainValidated(StudentInput student, CancellationToken cancellationToken = default);

	/// <summary>Validate the input and return the explained verdict as of an explicit reference date.</summary>
	ValidatedEvaluation<ExplainedResult> ExplainValidated(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	/// <inheritdoc cref="EnrolmentEngine.StaleChoices" />
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	IReadOnlyList<Subject> StaleChoices(StudentInput student, CancellationToken cancellationToken = default);

	/// <inheritdoc cref="EnrolmentEngine.ValidateFinalProgramme(StudentInput, CancellationToken)" />
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	ValidatedEvaluation<FinalProgramme> ValidateFinalProgramme(StudentInput student, CancellationToken cancellationToken = default);

	/// <inheritdoc cref="EnrolmentEngine.ValidateFinalProgramme(StudentInput, DateOnly, CancellationToken)" />
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	ValidatedEvaluation<FinalProgramme> ValidateFinalProgramme(
		StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);
}
