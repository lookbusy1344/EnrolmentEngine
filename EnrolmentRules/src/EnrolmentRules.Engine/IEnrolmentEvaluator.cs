namespace EnrolmentRules.Engine;

using Domain;

/// <summary>Evaluate and explain student verdicts without the counterfactual advisor surface.</summary>
public interface IEnrolmentEvaluator
{
	CatalogueData Catalogue { get; }

	QualificationScale Scale { get; }

	EnrolmentResult Evaluate(StudentInput student, CancellationToken cancellationToken = default);

	EnrolmentResult Evaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	ExplainedResult Explain(StudentInput student, CancellationToken cancellationToken = default);

	ExplainedResult Explain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	ValidatedEvaluation<EnrolmentResult> TryEvaluate(StudentInput student, CancellationToken cancellationToken = default);

	ValidatedEvaluation<EnrolmentResult> TryEvaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);

	ValidatedEvaluation<ExplainedResult> TryExplain(StudentInput student, CancellationToken cancellationToken = default);

	ValidatedEvaluation<ExplainedResult> TryExplain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default);
}
