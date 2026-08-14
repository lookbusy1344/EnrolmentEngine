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

	private IEnrolmentCriteriaExplainer Explainer => factory.Current;

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

	public AdviceResult Advise(StudentInput student, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default) =>
		Advisor.Advise(student, unsatGcses, cancellationToken);

	public AdviceResult Advise(
		StudentInput student,
		DateOnly asOf,
		UnsatGcseAdvice unsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.Advise(student, asOf, unsatGcses, cancellationToken);

	public ValidatedEvaluation<EnrolmentResult> EvaluateValidated(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.EvaluateValidated(student, cancellationToken);

	public ValidatedEvaluation<EnrolmentResult> EvaluateValidated(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Evaluator.EvaluateValidated(student, asOf, cancellationToken);

	public IReadOnlyList<Subject> StaleChoices(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.StaleChoices(student, cancellationToken);

	/// <summary>
	///     Resolved through the factory like every other call, so a reload that retunes a threshold or edits
	///     an entry rule changes the criteria this returns without a container rebuild.
	/// </summary>
	public SubjectCriteria Describe(Subject subject) => Explainer.Describe(subject);

	public ValidatedEvaluation<ExplainedResult> ExplainValidated(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluator.ExplainValidated(student, cancellationToken);

	public ValidatedEvaluation<ExplainedResult> ExplainValidated(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Evaluator.ExplainValidated(student, asOf, cancellationToken);

	public ValidatedEvaluation<AdviceResult> AdviseValidated(StudentInput student, CancellationToken cancellationToken = default) =>
		Advisor.AdviseValidated(student, cancellationToken);

	public ValidatedEvaluation<AdviceResult> AdviseValidated(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		Advisor.AdviseValidated(student, asOf, cancellationToken);

	public ValidatedEvaluation<AdviceResult> AdviseValidated(
		StudentInput student,
		UnsatGcseAdvice unsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.AdviseValidated(student, unsatGcses, cancellationToken);

	public ValidatedEvaluation<AdviceResult> AdviseValidated(
		StudentInput student,
		DateOnly asOf,
		UnsatGcseAdvice unsatGcses,
		CancellationToken cancellationToken = default) =>
		Advisor.AdviseValidated(student, asOf, unsatGcses, cancellationToken);
}
