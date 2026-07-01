namespace EnrolmentRules.Engine;

using Domain;
using Prediction;
using RulesEngine.Interfaces;

/// <summary>
///     The façade over the whole pipeline (§1.7): predict → engine (eligibility + per-subject tiers) →
///     constraint pass → optional green cap → aggregate, composed into an <see cref="EnrolmentResult" />.
///     Construct via <c>EnrolmentEngine.Create</c> or dependency-injection registration — not via <c>new</c>.
/// </summary>
public sealed class EnrolmentEngine : IEnrolmentEngine
{
	private readonly Func<DateOnly> asOf;
	private readonly RatingEvaluator evaluator;
	private readonly DfeTransitionMatrix matrix;

	/// <summary>Bind a fixed reference date: the parameterless overloads always evaluate as of <paramref name="asOf" />.</summary>
	internal EnrolmentEngine(RatingEvaluator evaluator, CatalogueData catalogue, DateOnly asOf, DfeTransitionMatrix? matrix = null)
		: this(evaluator, catalogue, () => asOf, matrix)
	{
	}

	/// <summary>
	///     Bind a live reference-date source: the parameterless <see cref="Evaluate(StudentInput, CancellationToken)" />,
	///     <see cref="Explain(StudentInput, CancellationToken)" /> and <see cref="Advise(StudentInput, CancellationToken)" /> overloads
	///     resolve <paramref name="asOf" /> afresh on every call, so a long-running singleton tracks the wall
	///     clock instead of freezing the date at construction. The engine stays stateless: the source is a pure
	///     read, and callers wanting an explicit date use the per-call overloads.
	/// </summary>
	internal EnrolmentEngine(RatingEvaluator evaluator, CatalogueData catalogue, Func<DateOnly> asOf, DfeTransitionMatrix? matrix = null)
	{
		this.evaluator = evaluator;
		Catalogue = evaluator.Catalogue;
		if (!ReferenceEquals(Catalogue, catalogue)) {
			throw new InvalidOperationException("The engine catalogue must match the evaluator catalogue.");
		}

		this.matrix = matrix ?? DfeTransitionMatrix.LoadDefault();
		this.asOf = asOf;
	}

	internal EnrolmentEngine(IRulesEngine engine, PolicyThresholds thresholds, DateOnly asOf)
		: this(engine, thresholds, Domain.Catalogue.Default, asOf, QualificationScale.Default)
	{
	}

	internal EnrolmentEngine(IRulesEngine engine, PolicyThresholds thresholds, CatalogueData catalogue, DateOnly asOf)
		: this(engine, thresholds, catalogue, asOf, QualificationScale.Default)
	{
	}

	internal EnrolmentEngine(
		IRulesEngine engine,
		PolicyThresholds thresholds,
		CatalogueData catalogue,
		DateOnly asOf,
		QualificationScale scale)
		: this(new(engine, thresholds, catalogue, scale), catalogue, asOf)
	{
	}

	/// <summary>
	///     The catalogue this engine evaluates against. Exposed so callers validating student input at the
	///     boundary (e.g. <c>StudentValidator</c>) honour the same table the engine holds rather than
	///     reloading or consulting the shipped <see cref="Domain.Catalogue.Default" />.
	/// </summary>
	public CatalogueData Catalogue { get; }

	/// <summary>The qualification scale this engine evaluates against.</summary>
	public QualificationScale Scale => evaluator.Scale;

	/// <summary>The whole-student §1.7 verdict (the document the golden-file suite locks), as of the bound date.</summary>
	public EnrolmentResult Evaluate(StudentInput student, CancellationToken cancellationToken = default) =>
		Evaluate(student, asOf(), cancellationToken);

	/// <summary>The whole-student §1.7 verdict as of an explicit reference date (per-request hosting).</summary>
	public EnrolmentResult Evaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		ToResult(Run(student, asOf, cancellationToken));

	/// <summary>The same verdict with per-recommendation provenance attached (<c>--explain</c>).</summary>
	public ExplainedResult Explain(StudentInput student, CancellationToken cancellationToken = default) =>
		Explain(student, asOf(), cancellationToken);

	/// <summary>The explained verdict as of an explicit reference date.</summary>
	public ExplainedResult Explain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		ToExplained(Run(student, asOf, cancellationToken));

	/// <summary>
	///     Counterfactual guidance over the same pipeline: for an eligible student, propose the minimal
	///     GCSE grade moves that would lift each amber/red subject to the next rating; for an ineligible
	///     student, propose the minimal bundle that clears the eligibility gate.
	/// </summary>
	public AdviceResult Advise(StudentInput student, CancellationToken cancellationToken = default) =>
		Advise(student, asOf(), cancellationToken);

	/// <summary>Counterfactual guidance as of an explicit reference date.</summary>
	public AdviceResult Advise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
		Advise(student, asOf, evaluator.Thresholds.AdviceConsidersUnsatGcses, cancellationToken);

	/// <summary>
	///     Counterfactual guidance with an explicit <paramref name="considerUnsatGcses" /> override of the
	///     loaded <see cref="PolicyThresholds.AdviceConsidersUnsatGcses" /> default — the diagnostic mode that
	///     lets the search also propose sitting GCSEs the student never took. As of the bound reference date.
	/// </summary>
	public AdviceResult Advise(StudentInput student, bool considerUnsatGcses, CancellationToken cancellationToken = default) =>
		Advise(student, asOf(), considerUnsatGcses, cancellationToken);

	/// <summary>Counterfactual guidance with an explicit diagnostic override, as of an explicit reference date.</summary>
	public AdviceResult Advise(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		CounterfactualAdvisor.Advise(this, student, evaluator.Thresholds, asOf, considerUnsatGcses, cancellationToken: cancellationToken);

	/// <inheritdoc cref="IEnrolmentEvaluator.TryEvaluate(StudentInput, CancellationToken)" />
	public ValidatedEvaluation<EnrolmentResult> TryEvaluate(StudentInput student, CancellationToken cancellationToken = default) =>
		TryEvaluate(student, asOf(), cancellationToken);

	/// <inheritdoc cref="IEnrolmentEvaluator.TryEvaluate(StudentInput, DateOnly, CancellationToken)" />
	public ValidatedEvaluation<EnrolmentResult> TryEvaluate(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
	{
		var validation = ValidateInput(student);
		return validation.IsValid
			? new(validation, Evaluate(student, asOf, cancellationToken))
			: new(validation, null);
	}

	/// <inheritdoc cref="IEnrolmentEvaluator.TryExplain(StudentInput, CancellationToken)" />
	public ValidatedEvaluation<ExplainedResult> TryExplain(StudentInput student, CancellationToken cancellationToken = default) =>
		TryExplain(student, asOf(), cancellationToken);

	/// <inheritdoc cref="IEnrolmentEvaluator.TryExplain(StudentInput, DateOnly, CancellationToken)" />
	public ValidatedEvaluation<ExplainedResult> TryExplain(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
	{
		var validation = ValidateInput(student);
		return validation.IsValid
			? new(validation, Explain(student, asOf, cancellationToken))
			: new(validation, null);
	}

	/// <inheritdoc cref="IEnrolmentAdvisor.TryAdvise(StudentInput, CancellationToken)" />
	public ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, CancellationToken cancellationToken = default) =>
		TryAdvise(student, asOf(), cancellationToken);

	/// <inheritdoc cref="IEnrolmentAdvisor.TryAdvise(StudentInput, DateOnly, CancellationToken)" />
	public ValidatedEvaluation<AdviceResult> TryAdvise(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		TryAdvise(student, asOf, evaluator.Thresholds.AdviceConsidersUnsatGcses, cancellationToken);

	/// <inheritdoc cref="IEnrolmentAdvisor.TryAdvise(StudentInput, bool, CancellationToken)" />
	public ValidatedEvaluation<AdviceResult> TryAdvise(
		StudentInput student,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default) =>
		TryAdvise(student, asOf(), considerUnsatGcses, cancellationToken);

	/// <inheritdoc cref="IEnrolmentAdvisor.TryAdvise(StudentInput, DateOnly, bool, CancellationToken)" />
	public ValidatedEvaluation<AdviceResult> TryAdvise(
		StudentInput student,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default)
	{
		var validation = ValidateInput(student);
		return validation.IsValid
			? new(validation, Advise(student, asOf, considerUnsatGcses, cancellationToken))
			: new(validation, null);
	}

	/// <summary>
	///     Create a fully bootstrapped engine from the shipped layout: thresholds, catalogue, workflows,
	///     probe compilation and the reusable façade. Intended for long-running hosts and other library
	///     consumers that should not reimplement the bootstrap recipe.
	/// </summary>
	/// <exception cref="WorkflowException">A workflow file failed schema validation or probe compilation.</exception>
	/// <exception cref="CatalogueException">The catalogue failed schema validation or load-time invariant checks.</exception>
	/// <exception cref="PolicyThresholdsException">The thresholds file failed schema validation or load-time invariant checks.</exception>
	public static EnrolmentEngine Create(
		string workflowsDirectory,
		string dataDirectory,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
		=> Create(new DirectoryDataSource(workflowsDirectory, dataDirectory), asOf, cancellationToken);

	/// <inheritdoc cref="Create(string, string, DateOnly, CancellationToken)" />
	/// <remarks>
	///     The <paramref name="asOf" /> source is resolved per evaluation, so a singleton built this way tracks
	///     a live clock (e.g. <c>() =&gt; DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)</c>).
	/// </remarks>
	public static EnrolmentEngine Create(
		string workflowsDirectory,
		string dataDirectory,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
		=> Create(new DirectoryDataSource(workflowsDirectory, dataDirectory), asOf, cancellationToken);

	/// <summary>
	///     Create a fully bootstrapped engine from a stream-backed data source. This is the shared startup
	///     path for directory-backed hosts, embedded-resource hosts, and tests that want to keep the data
	///     off disk.
	/// </summary>
	/// <exception cref="WorkflowException">A workflow file failed schema validation or probe compilation.</exception>
	/// <exception cref="CatalogueException">The catalogue failed schema validation or load-time invariant checks.</exception>
	/// <exception cref="PolicyThresholdsException">The thresholds file failed schema validation or load-time invariant checks.</exception>
	public static EnrolmentEngine Create(
		IEnrolmentDataSource source,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
		=> Create(source, () => asOf, cancellationToken);

	/// <inheritdoc cref="Create(IEnrolmentDataSource, DateOnly, CancellationToken)" />
	/// <remarks>
	///     The <paramref name="asOf" /> source is resolved per evaluation, so a singleton built this way tracks
	///     a live clock (e.g. <c>() =&gt; DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)</c>).
	/// </remarks>
	public static EnrolmentEngine Create(
		IEnrolmentDataSource source,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		cancellationToken.ThrowIfCancellationRequested();
		using var thresholdsStream = source.OpenThresholds();
		using var thresholdsSchemaStream = source.OpenThresholdsSchema();
		var thresholds = PolicyThresholdsStore.LoadAndValidate(thresholdsStream, thresholdsSchemaStream);
		cancellationToken.ThrowIfCancellationRequested();
		using var qualificationsStream = source.OpenQualifications();
		using var qualificationsSchemaStream = source.OpenQualificationsSchema();
		var scale = QualificationScaleStore.LoadAndValidate(qualificationsStream, qualificationsSchemaStream);
		cancellationToken.ThrowIfCancellationRequested();
		using var catalogueStream = source.OpenCatalogue();
		using var catalogueSchemaStream = source.OpenCatalogueSchema();
		var catalogue = CatalogueStore.LoadAndValidate(catalogueStream, catalogueSchemaStream, scale);
		cancellationToken.ThrowIfCancellationRequested();
		using var matrixStream = source.OpenTransitionMatrix();
		var matrix = DfeTransitionMatrix.Load(matrixStream);
		cancellationToken.ThrowIfCancellationRequested();
		var workflowFiles = source.OpenWorkflows();
		try {
			using var workflowSchemaStream = source.OpenWorkflowSchema();
			var engine = WorkflowStore.LoadValidateBuildAndProbe(workflowFiles, workflowSchemaStream, catalogue, thresholds, matrix, scale);
			cancellationToken.ThrowIfCancellationRequested();
			return new(new(engine, thresholds, catalogue, scale), catalogue, asOf, matrix);
		}
		finally {
			foreach (var workflow in workflowFiles) {
				workflow.Dispose();
			}
		}
	}

	/// <summary>
	///     Run the pipeline once. Order is fixed (predict → engine → constraints → cap → aggregate): the cap
	///     (optional, a no-op unless a green cap is configured) counts the greens that survived the constraint
	///     downgrades, so it must read the constraint pass's result. The two adjustment stages compose by
	///     most-severe-wins into the final ratings.
	/// </summary>
	private Evaluation Run(StudentInput student, DateOnly asOf, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var gcses = student.ToGcseResults();
		var profile = GradePredictor.Predict(student, gcses, asOf, Catalogue, matrix, Scale);
		cancellationToken.ThrowIfCancellationRequested();
		var (gate, baseRatings) = evaluator.EvaluateWithGate(profile, gcses, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		var constraintAdjustments = ConstraintPass.Evaluate(baseRatings, profile, Catalogue);
		var afterConstraints = ConstraintPass.Apply(baseRatings, constraintAdjustments);
		cancellationToken.ThrowIfCancellationRequested();

		var capAdjustments = Aggregator.CapGreens(afterConstraints, Catalogue, evaluator.Thresholds);
		var finalRatings = ConstraintPass.Apply(afterConstraints, capAdjustments);
		cancellationToken.ThrowIfCancellationRequested();

		EquatableArray<Adjustment> adjustments = [.. constraintAdjustments, .. capAdjustments];
		return new(profile, gate, [.. baseRatings], [.. finalRatings], adjustments,
			Aggregator.Summarise(finalRatings, Catalogue, evaluator.Thresholds));
	}

	private ValidationOutcome ValidateInput(StudentInput student) =>
		new([.. StudentValidator.Validate(student, Catalogue, Scale)]);

	private EnrolmentResult ToResult(Evaluation e) =>
		new(
			e.Gate.Eligible,
			[.. e.Gate.Reasons],
			[.. Aggregator.Rank(e.FinalRatings, Catalogue).Select(static r => new Recommendation(r.Subject, r.Rating, r.Reason))],
			e.Summary,
			[.. e.Adjustments]);

	private ExplainedResult ToExplained(Evaluation e)
	{
		var baseBySubject = e.BaseRatings.ToDictionary(static r => r.Subject);
		var overridesBySubject = e.Adjustments.ToLookup(static a => a.Subject);
		var predicted = e.Profile.PredictedGrades.ToDictionary(static p => p.Subject, static p => p.PredictedPoints);

		// An ineligible student's reds come from the gate short-circuit, not a fired subject rule, so the
		// provenance names the eligibility workflow rather than a fabricated "{subject}:red" rule.
		string Rule(SubjectRating @base) =>
			e.Gate.Eligible ? RatingEvaluator.RuleName(@base.Subject, @base.Rating) : RatingEvaluator.EligibilityWorkflow;

		return new(
			e.Gate.Eligible,
			[.. e.Gate.Reasons],
			[
				.. Aggregator.Rank(e.FinalRatings, Catalogue).Select(r => {
					var @base = baseBySubject[r.Subject];
					return new Explanation(
						r.Subject, r.Rating, r.Reason,
						@base.Rating, Rule(@base), @base.Reason,
						predicted.GetValueOrDefault(r.Subject, ALevelGrade.Min),
						[.. overridesBySubject[r.Subject]]) { EntryEquivalentReason = EntryEquivalentReason(e.Profile, r.Subject, Catalogue) };
				}),
			],
			e.Summary);
	}

	private string? EntryEquivalentReason(StudentProfile profile, Subject subject, CatalogueData catalogue)
	{
		foreach (var equivalent in catalogue.Meta(subject).EntryEquivalents) {
			var match = profile.PriorQualifications.FirstOrDefault(qualification => Scale.Satisfies(qualification, equivalent));
			if (match != default) {
				return
					$"Entry equivalent satisfied by prior qualification {match.Subject} {EnumNames.NameOf(match.Type)} {match.Grade} for {EnumNames.NameOf(subject)}.";
			}
		}

		return null;
	}
}

/// <summary>
///     One run of the pipeline, captured before projection: the prediction <see cref="Profile" />, the
///     eligibility <see cref="Gate" />, the engine's <see cref="BaseRatings" />, the
///     <see cref="FinalRatings" /> after all host-code adjustments, the full <see cref="Adjustments" />
///     trail and the aggregate <see cref="Summary" />. Both the plain and explained results are pure
///     projections of this, so they never re-evaluate.
/// </summary>
internal sealed record Evaluation(
	StudentProfile Profile,
	EligibilityGate Gate,
	EquatableArray<SubjectRating> BaseRatings,
	EquatableArray<SubjectRating> FinalRatings,
	EquatableArray<Adjustment> Adjustments,
	EnrolmentSummary Summary);
