namespace EnrolmentRules.Extensions.DependencyInjection;

using Domain;
using Engine;

/// <summary>
///     Stable DI singleton that forwards every call to the factory's current engine so reloads are visible
///     without rebuilding the container.
/// </summary>
internal sealed class ReloadingEnrolmentEngineProxy(IEnrolmentEngineFactory factory) : IEnrolmentEngine
{
	private IEnrolmentEvaluator Evaluator => factory.Current;

	private IEnrolmentAdvisor Advisor => factory.Current;

	public CatalogueData Catalogue => Evaluator.Catalogue;

	public QualificationScale Scale => Evaluator.Scale;

	public Task<EnrolmentResult> EvaluateAsync(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.EvaluateAsync(student, cancellationToken);

	public Task<EnrolmentResult> EvaluateAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		Evaluator.EvaluateAsync(student, asOf, cancellationToken);

	public Task<ExplainedResult> ExplainAsync(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.ExplainAsync(student, cancellationToken);

	public Task<ExplainedResult> ExplainAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		Evaluator.ExplainAsync(student, asOf, cancellationToken);

	public Task<AdviceResult> AdviseAsync(StudentInput student, CancellationToken cancellationToken = default) =>
		Advisor.AdviseAsync(student, cancellationToken);

	public Task<AdviceResult> AdviseAsync(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		Advisor.AdviseAsync(student, asOf, cancellationToken);

	public Task<AdviceResult> AdviseAsync(StudentInput student, bool considerUnsatGcses, CancellationToken cancellationToken = default) =>
		Advisor.AdviseAsync(student, considerUnsatGcses, cancellationToken);

	public Task<AdviceResult> AdviseAsync(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.AdviseAsync(student, asOf, considerUnsatGcses, cancellationToken);

	public Task<ValidatedEvaluation<EnrolmentResult>> TryEvaluateAsync(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.TryEvaluateAsync(student, cancellationToken);

	public Task<ValidatedEvaluation<EnrolmentResult>> TryEvaluateAsync(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Evaluator.TryEvaluateAsync(student, asOf, cancellationToken);

	public Task<ValidatedEvaluation<ExplainedResult>> TryExplainAsync(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.TryExplainAsync(student, cancellationToken);

	public Task<ValidatedEvaluation<ExplainedResult>> TryExplainAsync(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Evaluator.TryExplainAsync(student, asOf, cancellationToken);

	public Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(StudentInput student, CancellationToken cancellationToken = default) =>
		Advisor.TryAdviseAsync(student, cancellationToken);

	public Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Advisor.TryAdviseAsync(student, asOf, cancellationToken);

	public Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(
		StudentInput student,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.TryAdviseAsync(student, considerUnsatGcses, cancellationToken);

	public Task<ValidatedEvaluation<AdviceResult>> TryAdviseAsync(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.TryAdviseAsync(student, asOf, considerUnsatGcses, cancellationToken);
}
