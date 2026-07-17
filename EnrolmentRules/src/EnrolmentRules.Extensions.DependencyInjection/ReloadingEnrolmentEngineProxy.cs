namespace EnrolmentRules.Extensions.DependencyInjection;

using Domain;

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

	public PolicyThresholds Thresholds => Evaluator.Thresholds;

	public EnrolmentResult Evaluate(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.Evaluate(student, cancellationToken);

	public EnrolmentResult Evaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		Evaluator.Evaluate(student, asOf, cancellationToken);

	public ExplainedResult Explain(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.Explain(student, cancellationToken);

	public ExplainedResult Explain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		Evaluator.Explain(student, asOf, cancellationToken);

	public AdviceResult Advise(StudentInput student, CancellationToken cancellationToken = default) =>
		Advisor.Advise(student, cancellationToken);

	public AdviceResult Advise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		Advisor.Advise(student, asOf, cancellationToken);

	public AdviceResult Advise(StudentInput student, bool considerUnsatGcses, CancellationToken cancellationToken = default) =>
		Advisor.Advise(student, considerUnsatGcses, cancellationToken);

	public AdviceResult Advise(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.Advise(student, asOf, considerUnsatGcses, cancellationToken);

	public ValidatedEvaluation<EnrolmentResult> TryEvaluate(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.TryEvaluate(student, cancellationToken);

	public ValidatedEvaluation<EnrolmentResult> TryEvaluate(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Evaluator.TryEvaluate(student, asOf, cancellationToken);

	public IReadOnlyList<Subject> StaleChoices(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.StaleChoices(student, cancellationToken);

	public ValidatedEvaluation<ExplainedResult> TryExplain(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.TryExplain(student, cancellationToken);

	public ValidatedEvaluation<ExplainedResult> TryExplain(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Evaluator.TryExplain(student, asOf, cancellationToken);

	public ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, CancellationToken cancellationToken = default) =>
		Advisor.TryAdvise(student, cancellationToken);

	public ValidatedEvaluation<AdviceResult> TryAdvise(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Advisor.TryAdvise(student, asOf, cancellationToken);

	public ValidatedEvaluation<AdviceResult> TryAdvise(
		StudentInput student,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.TryAdvise(student, considerUnsatGcses, cancellationToken);

	public ValidatedEvaluation<AdviceResult> TryAdvise(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.TryAdvise(student, asOf, considerUnsatGcses, cancellationToken);
}
