namespace EnrolmentRules.Engine;

using Domain;

/// <summary>Evaluate and explain student verdicts without the counterfactual advisor surface.</summary>
public interface IEnrolmentEvaluator
{
	CatalogueData Catalogue { get; }

	QualificationScale Scale { get; }

	Task<EnrolmentResult> EvaluateAsync(StudentInput student, CancellationToken cancellationToken = default);

	Task<EnrolmentResult> EvaluateAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	Task<ExplainedResult> ExplainAsync(StudentInput student, CancellationToken cancellationToken = default);

	Task<ExplainedResult> ExplainAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<EnrolmentResult>> TryEvaluateAsync(StudentInput student, CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<EnrolmentResult>> TryEvaluateAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<ExplainedResult>> TryExplainAsync(StudentInput student, CancellationToken cancellationToken = default);

	Task<ValidatedEvaluation<ExplainedResult>> TryExplainAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);
}
