namespace EnrolmentRules.Engine;

using Domain;
using Prediction;
using RulesEngine.Interfaces;
using RulesEngine.Models;

/// <summary>
///     The façade over the whole pipeline (§1.7): predict → engine (eligibility + per-subject tiers) →
///     constraint pass → optional green cap → aggregate, composed into an <see cref="EnrolmentResult" />.
///     Construct via <c>EnrolmentEngine.Create</c> or dependency-injection registration — not via <c>new</c>.
/// </summary>
public sealed class EnrolmentEngine : IEnrolmentEngine
{
	private readonly Func<DateOnly> asOf;
	private readonly CriteriaExplainer? criteria;
	private readonly RatingEvaluator evaluator;
	private readonly DfeTransitionMatrix matrix;

	/// <summary>Bind a fixed reference date: the parameterless overloads always evaluate as of <paramref name="asOf" />.</summary>
	internal EnrolmentEngine(
		RatingEvaluator evaluator,
		CatalogueData catalogue,
		DateOnly asOf,
		DfeTransitionMatrix? matrix = null,
		IReadOnlyList<Workflow>? workflows = null)
		: this(evaluator, catalogue, () => asOf, matrix, workflows) { }

	/// <summary>
	///     Bind a live reference-date source: the parameterless <see cref="Evaluate(StudentInput, CancellationToken)" />,
	///     <see cref="Explain(StudentInput, CancellationToken)" /> and <see cref="Advise(StudentInput, CancellationToken)" /> overloads
	///     resolve <paramref name="asOf" /> afresh on every call, so a long-running singleton tracks the wall
	///     clock instead of freezing the date at construction. The engine stays stateless: the source is a pure
	///     read, and callers wanting an explicit date use the per-call overloads.
	/// </summary>
	internal EnrolmentEngine(
		RatingEvaluator evaluator,
		CatalogueData catalogue,
		Func<DateOnly> asOf,
		DfeTransitionMatrix? matrix = null,
		IReadOnlyList<Workflow>? workflows = null)
	{
		this.evaluator = evaluator;
		criteria = workflows is null ? null : new(workflows, evaluator.Thresholds, evaluator.Catalogue);
		Catalogue = evaluator.Catalogue;
		if (!ReferenceEquals(Catalogue, catalogue)) {
			throw new InvalidOperationException("The engine catalogue must match the evaluator catalogue.");
		}

		this.matrix = matrix ?? DfeTransitionMatrix.LoadDefault();
		this.asOf = asOf;
	}

	internal EnrolmentEngine(IRulesEngine engine, PolicyThresholds thresholds, DateOnly asOf)
		: this(engine, thresholds, Domain.Catalogue.Default, asOf, QualificationScale.Default) { }

	internal EnrolmentEngine(IRulesEngine engine, PolicyThresholds thresholds, CatalogueData catalogue, DateOnly asOf)
		: this(engine, thresholds, catalogue, asOf, QualificationScale.Default) { }

	internal EnrolmentEngine(
		IRulesEngine engine,
		PolicyThresholds thresholds,
		CatalogueData catalogue,
		DateOnly asOf,
		QualificationScale scale,
		IReadOnlyList<Workflow>? workflows = null)
		: this(new(engine, thresholds, catalogue, scale), catalogue, asOf, null, workflows) { }

	/// <summary>The per-call default: the loaded <see cref="PolicyThresholds.AdviceConsidersUnsatGcses" /> knob, as a scope.</summary>
	private UnsatGcseAdvice DefaultUnsatGcseAdvice =>
		evaluator.Thresholds.AdviceConsidersUnsatGcses ? UnsatGcseAdvice.IncludeUnsat : UnsatGcseAdvice.HeldOnly;

	/// <summary>
	///     The catalogue this engine evaluates against. Exposed so callers validating student input at the
	///     boundary (e.g. <c>StudentValidator</c>) honour the same table the engine holds rather than
	///     reloading or consulting the shipped <see cref="Domain.Catalogue.Default" />.
	/// </summary>
	public CatalogueData Catalogue { get; }

	/// <summary>The qualification scale this engine evaluates against.</summary>
	public QualificationScale Scale => evaluator.Scale;

	/// <summary>The policy knobs (choice caps, entry bands, etc.) this engine evaluates against.</summary>
	public PolicyThresholds Thresholds => evaluator.Thresholds;

	/// <inheritdoc />
	/// <remarks>
	///     Narrated from the same workflow graph this engine evaluates, so the criteria a student is shown
	///     and the rules that decide their rating can never be different revisions.
	/// </remarks>
	public SubjectCriteria Describe(Subject subject) =>
		criteria is null
			? throw new InvalidOperationException(
				"This engine was constructed without its workflow graph, so its rules cannot be described. "
				+ "Build it through EnrolmentEngine.Create (or the DI registration), which always supplies one.")
			: criteria.Describe(subject);

	/// <summary>The whole-student §1.7 verdict (the document the golden-file suite locks), as of the bound date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public EnrolmentResult Evaluate(StudentInput student, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		return Evaluate(student, asOf(), cancellationToken);
	}

	/// <summary>The whole-student §1.7 verdict as of an explicit reference date (per-request hosting).</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public EnrolmentResult Evaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		return ToResult(Run(student, asOf, cancellationToken));
	}

	/// <summary>The same verdict with per-recommendation provenance attached (<c>--explain</c>).</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public ExplainedResult Explain(StudentInput student, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		return Explain(student, asOf(), cancellationToken);
	}

	/// <summary>The explained verdict as of an explicit reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public ExplainedResult Explain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		return ToExplained(Run(student, asOf, cancellationToken));
	}

	/// <summary>
	///     Counterfactual guidance over the same pipeline: for an eligible student, propose the minimal
	///     GCSE grade moves that would lift each amber/red subject to the next rating; for an ineligible
	///     student, propose the minimal bundle that clears the eligibility gate.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public AdviceResult Advise(StudentInput student, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		return Advise(student, asOf(), cancellationToken);
	}

	/// <summary>Counterfactual guidance as of an explicit reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public AdviceResult Advise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		return Advise(student, asOf, DefaultUnsatGcseAdvice, cancellationToken);
	}

	/// <summary>
	///     Counterfactual guidance with an explicit <paramref name="unsatGcses" /> override of the loaded
	///     <see cref="PolicyThresholds.AdviceConsidersUnsatGcses" /> default — the diagnostic mode that lets the
	///     search also propose sitting GCSEs the student never took. As of the bound reference date.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="unsatGcses" /> is not a supported advice scope.</exception>
	public AdviceResult Advise(StudentInput student, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		ValidateUnsatGcseAdvice(unsatGcses);
		return Advise(student, asOf(), unsatGcses, cancellationToken);
	}

	/// <summary>Counterfactual guidance with an explicit diagnostic override, as of an explicit reference date.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="unsatGcses" /> is not a supported advice scope.</exception>
	public AdviceResult Advise(
		StudentInput student,
		DateOnly asOf,
		UnsatGcseAdvice unsatGcses,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		ValidateUnsatGcseAdvice(unsatGcses);
		return CounterfactualAdvisor.Advise(
			this, student, evaluator.Thresholds, asOf, unsatGcses is UnsatGcseAdvice.IncludeUnsat, cancellationToken: cancellationToken);
	}

	/// <summary>
	///     The committed choices the student may no longer hold: every <c>chosen_a_levels</c> entry the
	///     pipeline now rates red. A choice is only ever made against a green or amber subject, so a red one
	///     means the facts moved underneath it after it was committed — the <c>*Validated</c> calls refuse any
	///     document that still names one, and a caller holding a mutable basket (both web front ends) prunes
	///     against this list and re-evaluates rather than presenting the refusal to the student.
	/// </summary>
	/// <remarks>
	///     Returns empty for a student with no choices, and for one whose facts do not validate — there is no
	///     trustworthy rating to prune against, and the caller will surface the facts' own validation errors
	///     from a <c>*Validated</c> call anyway. One pass suffices: dropping choices only removes downgrades, so no
	///     surviving choice can newly turn red.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public IReadOnlyList<Subject> StaleChoices(StudentInput student, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		return student.ChosenALevels.Count == 0 || !ValidateInput(student).IsValid
			? []
			: [.. StaleChoiceRatings(Run(student, asOf(), cancellationToken)).Select(static rating => rating.Subject)];
	}

	/// <summary>
	///     The final-programme boundary (§1.5): distinct from <see cref="StudentValidator" />'s incremental
	///     basket checks, which must accept a partially-built or empty selection while a student is still
	///     editing it. This is the explicit "is this a submittable programme" gate a caller invokes only when
	///     finalising, checking — in addition to the structural facts validation every <c>*Validated</c> call
	///     applies — that the choice count sits within <see cref="PolicyThresholds.MinChosenALevels" /> and
	///     the effective high-attainment-aware maximum (<see cref="Aggregator.EffectiveMaxChosenALevels(StudentProfile, PolicyThresholds)" />),
	///     and that no chosen subject has gone red (the same stale-choice guard <see cref="EvaluateValidated(StudentInput, CancellationToken)" />
	///     applies). Amber remains permitted, matching the existing Choose gate. Duplicate and not-offered
	///     chosen subjects are already rejected by the structural facts validation.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public ValidatedEvaluation<FinalProgramme> ValidateFinalProgramme(
		StudentInput student, CancellationToken cancellationToken = default) =>
		ValidateFinalProgramme(student, asOf(), cancellationToken);

	/// <inheritdoc cref="ValidateFinalProgramme(StudentInput, CancellationToken)" />
	/// <exception cref="ArgumentNullException"><paramref name="student" /> is null.</exception>
	public ValidatedEvaluation<FinalProgramme> ValidateFinalProgramme(
		StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		var validation = ValidateInput(student);
		if (!validation.IsValid) {
			return new(validation, null);
		}

		var evaluation = Run(student, asOf, cancellationToken);
		var min = evaluator.Thresholds.MinChosenALevels;
		var max = Aggregator.EffectiveMaxChosenALevels(evaluation.Profile, evaluator.Thresholds);
		var count = student.ChosenALevels.Count;

		List<string> errors = [.. RejectedChoices(evaluation)];
		if (count < min) {
			errors.Add($"final programme requires at least {min} distinct A-level choice(s); {count} were made");
		}

		if (count > max) {
			errors.Add($"final programme allows at most {max} distinct A-level choice(s); {count} were made");
		}

		return errors.Count > 0
			? new(new([.. errors]), null)
			: new(validation, new(student.ChosenALevels, min, max));
	}

	/// <inheritdoc cref="IEnrolmentEvaluator.EvaluateValidated(StudentInput, CancellationToken)" />
	public ValidatedEvaluation<EnrolmentResult> EvaluateValidated(StudentInput student, CancellationToken cancellationToken = default) =>
		RunValidated(student, asOf(), ToResult, cancellationToken);

	/// <inheritdoc cref="IEnrolmentEvaluator.EvaluateValidated(StudentInput, DateOnly, CancellationToken)" />
	public ValidatedEvaluation<EnrolmentResult> EvaluateValidated(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		RunValidated(student, asOf, ToResult, cancellationToken);

	/// <inheritdoc cref="IEnrolmentEvaluator.ExplainValidated(StudentInput, CancellationToken)" />
	public ValidatedEvaluation<ExplainedResult> ExplainValidated(StudentInput student, CancellationToken cancellationToken = default) =>
		RunValidated(student, asOf(), ToExplained, cancellationToken);

	/// <inheritdoc cref="IEnrolmentEvaluator.ExplainValidated(StudentInput, DateOnly, CancellationToken)" />
	public ValidatedEvaluation<ExplainedResult> ExplainValidated(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		RunValidated(student, asOf, ToExplained, cancellationToken);

	/// <inheritdoc cref="IEnrolmentAdvisor.AdviseValidated(StudentInput, CancellationToken)" />
	public ValidatedEvaluation<AdviceResult> AdviseValidated(StudentInput student, CancellationToken cancellationToken = default) =>
		AdviseValidated(student, asOf(), DefaultUnsatGcseAdvice, cancellationToken);

	/// <inheritdoc cref="IEnrolmentAdvisor.AdviseValidated(StudentInput, DateOnly, CancellationToken)" />
	public ValidatedEvaluation<AdviceResult> AdviseValidated(
		StudentInput student,
		DateOnly asOf,
		CancellationToken cancellationToken = default) =>
		AdviseValidated(student, asOf, DefaultUnsatGcseAdvice, cancellationToken);

	/// <inheritdoc cref="IEnrolmentAdvisor.AdviseValidated(StudentInput, UnsatGcseAdvice, CancellationToken)" />
	public ValidatedEvaluation<AdviceResult> AdviseValidated(
		StudentInput student,
		UnsatGcseAdvice unsatGcses,
		CancellationToken cancellationToken = default)
	{
		ValidateUnsatGcseAdvice(unsatGcses);
		return AdviseValidated(student, asOf(), unsatGcses, cancellationToken);
	}

	/// <inheritdoc cref="IEnrolmentAdvisor.AdviseValidated(StudentInput, DateOnly, UnsatGcseAdvice, CancellationToken)" />
	/// <remarks>
	///     Deliberately exempt from the stale-choice guard the other <c>*Validated</c> entry points apply. Those
	///     answer "what is this student's verdict", and a red committed choice has no defensible answer. This
	///     one answers "what would have to change", which is precisely the question a student with a red
	///     choice needs answered — refusing the document would withhold the advice from the only people who
	///     need it. Nothing is accepted by advising: it returns counterfactuals, never an enrolment.
	/// </remarks>
	public ValidatedEvaluation<AdviceResult> AdviseValidated(
		StudentInput student,
		DateOnly asOf,
		UnsatGcseAdvice unsatGcses,
		CancellationToken cancellationToken = default)
	{
		ValidateUnsatGcseAdvice(unsatGcses);
		var validation = ValidateInput(student);
		return validation.IsValid
			? new(validation, Advise(student, asOf, unsatGcses, cancellationToken))
			: new(validation, null);
	}

	private static void ValidateUnsatGcseAdvice(UnsatGcseAdvice unsatGcses)
	{
		if (unsatGcses is not (UnsatGcseAdvice.HeldOnly or UnsatGcseAdvice.IncludeUnsat)) {
			throw new ArgumentOutOfRangeException(
				nameof(unsatGcses), unsatGcses, "Value must identify a supported unsat GCSE advice scope.");
		}
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
	/// <exception cref="ArgumentNullException"><paramref name="source" /> is null.</exception>
	/// <exception cref="InvalidOperationException"><paramref name="source" /> returned null from a stream- or workflow-opening member.</exception>
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
	/// <exception cref="ArgumentNullException"><paramref name="asOf" /> is null.</exception>
	public static EnrolmentEngine Create(
		IEnrolmentDataSource source,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(asOf);
		cancellationToken.ThrowIfCancellationRequested();
		using var thresholdsStream = RequireStream(source.OpenThresholds(), nameof(IEnrolmentDataSource.OpenThresholds));
		using var thresholdsSchemaStream = RequireStream(source.OpenThresholdsSchema(), nameof(IEnrolmentDataSource.OpenThresholdsSchema));
		var thresholds = PolicyThresholdsStore.LoadAndValidate(thresholdsStream, thresholdsSchemaStream);
		cancellationToken.ThrowIfCancellationRequested();
		using var qualificationsStream = RequireStream(source.OpenQualifications(), nameof(IEnrolmentDataSource.OpenQualifications));
		using var qualificationsSchemaStream =
			RequireStream(source.OpenQualificationsSchema(), nameof(IEnrolmentDataSource.OpenQualificationsSchema));
		var scale = QualificationScaleStore.LoadAndValidate(qualificationsStream, qualificationsSchemaStream);
		cancellationToken.ThrowIfCancellationRequested();
		using var catalogueStream = RequireStream(source.OpenCatalogue(), nameof(IEnrolmentDataSource.OpenCatalogue));
		using var catalogueSchemaStream = RequireStream(source.OpenCatalogueSchema(), nameof(IEnrolmentDataSource.OpenCatalogueSchema));
		var catalogue = CatalogueStore.LoadAndValidate(catalogueStream, catalogueSchemaStream, scale);
		cancellationToken.ThrowIfCancellationRequested();
		using var matrixStream = RequireStream(source.OpenTransitionMatrix(), nameof(IEnrolmentDataSource.OpenTransitionMatrix));
		var matrix = DfeTransitionMatrix.Load(matrixStream);
		matrix.ValidateCoverage(catalogue);
		cancellationToken.ThrowIfCancellationRequested();
		var workflowFiles = RequireWorkflows(source.OpenWorkflows());
		try {
			using var workflowSchemaStream = RequireStream(source.OpenWorkflowSchema(), nameof(IEnrolmentDataSource.OpenWorkflowSchema));
			var built = WorkflowStore.LoadValidateBuildAndProbe(workflowFiles, workflowSchemaStream, catalogue, thresholds, matrix, scale);
			cancellationToken.ThrowIfCancellationRequested();
			return new(new(built.Engine, thresholds, catalogue, scale), catalogue, asOf, matrix, built.Workflows);
		}
		finally {
			foreach (var workflow in workflowFiles) {
				workflow.Dispose();
			}
		}
	}

	/// <summary>
	///     Guards against a non-conformant <see cref="IEnrolmentDataSource" /> returning <see langword="null" />
	///     from a stream-opening member: converts what would otherwise be a downstream null dereference into an
	///     <see cref="InvalidOperationException" /> naming the offending member.
	/// </summary>
	private static Stream RequireStream(Stream? stream, string memberName) =>
		stream ?? throw new InvalidOperationException(
			$"{nameof(IEnrolmentDataSource)}.{memberName}() returned null; a data source must always return a stream.");

	/// <inheritdoc cref="RequireStream(Stream?, string)" />
	private static IReadOnlyList<WorkflowContent> RequireWorkflows(IReadOnlyList<WorkflowContent>? workflows) =>
		workflows ?? throw new InvalidOperationException(
			$"{nameof(IEnrolmentDataSource)}.{nameof(IEnrolmentDataSource.OpenWorkflows)}() returned null; " +
			"a data source must always return a workflow list.");

	/// <summary>
	///     Run the pipeline once. Order is fixed (predict → engine → constraints → chosen-subject cap →
	///     green cap → aggregate): both caps read the post-constraint ratings, and the optional green cap
	///     counts greens that survived every earlier downgrade. The adjustment stages compose by
	///     most-severe-wins into the final ratings.
	/// </summary>
	private Evaluation Run(StudentInput student, DateOnly asOf, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var gcses = student.ToGcseResults();
		var lookup = new GcseFacts(gcses);
		var profile = GradePredictor.Predict(student, gcses, asOf, Catalogue, matrix, Scale);
		cancellationToken.ThrowIfCancellationRequested();
		var (gate, baseRatings) = evaluator.EvaluateWithGate(profile, gcses, lookup, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		var constraintAdjustments = ConstraintPass.Evaluate(baseRatings, profile, Catalogue, Scale);
		var afterConstraints = ConstraintPass.Apply(baseRatings, constraintAdjustments);
		cancellationToken.ThrowIfCancellationRequested();

		var chosenSubjectCapAdjustments = Aggregator.CapChosenSubjects(afterConstraints, profile, evaluator.Thresholds);
		var afterChosenSubjectCap = ConstraintPass.Apply(afterConstraints, chosenSubjectCapAdjustments);
		var greenCapAdjustments = Aggregator.CapGreens(afterChosenSubjectCap, Catalogue, evaluator.Thresholds);
		var finalRatings = ConstraintPass.Apply(afterChosenSubjectCap, greenCapAdjustments);
		cancellationToken.ThrowIfCancellationRequested();

		EquatableArray<Adjustment> adjustments = [.. constraintAdjustments, .. chosenSubjectCapAdjustments, .. greenCapAdjustments];
		return new(profile, gate, [.. baseRatings], [.. finalRatings], adjustments,
			Aggregator.Summarise(finalRatings, Catalogue, evaluator.Thresholds));
	}

	/// <summary>
	///     The shared validated boundary: validate the document, run the pipeline once, refuse it if any
	///     committed choice has gone red, and only then project the run. The pipeline runs at most once —
	///     <paramref name="project" /> is a pure projection of the same <see cref="Evaluation" /> the
	///     stale-choice guard inspected, so the verdict and the guard can never disagree.
	/// </summary>
	private ValidatedEvaluation<T> RunValidated<T>(
		StudentInput student,
		DateOnly asOf,
		Func<Evaluation, T> project,
		CancellationToken cancellationToken)
		where T : class
	{
		var validation = ValidateInput(student);
		if (!validation.IsValid) {
			return new(validation, null);
		}

		var evaluation = Run(student, asOf, cancellationToken);
		var rejected = RejectedChoices(evaluation);
		return rejected.Count > 0
			? new(new([.. rejected]), null)
			: new(validation, project(evaluation));
	}

	/// <summary>
	///     The stale-choice guard: a <c>chosen_a_levels</c> entry the pipeline rates red is not a choice the
	///     student may hold. A commitment is only ever made against a subject that rated green or amber, so a
	///     red one means the facts moved underneath it — lower GCSEs, a new blocking hobby, a prior
	///     qualification, or a sibling choice that excludes it. The document is then self-inconsistent: it
	///     asserts a commitment the rules forbid, and honouring it would enrol the student on a course they
	///     are barred from. It is reported as a validation error rather than an adjustment because there is no
	///     defensible verdict to return — the caller must drop the choice and re-evaluate.
	/// </summary>
	/// <remarks>
	///     This cannot live in <see cref="StudentValidator" /> alongside the other <c>chosen_a_levels</c>
	///     checks: a rating exists only after prediction, the engine and the constraint pass have run, and the
	///     constraint pass reads <c>ChosenALevels</c> as an input. So it is a post-run check that reports
	///     <em>as</em> input validation. Pruning terminates in one pass: dropping choices only ever removes
	///     downgrades (the pass is monotone), so ratings can only improve and no surviving choice can newly
	///     turn red.
	/// </remarks>
	private static IReadOnlyList<SubjectRating> StaleChoiceRatings(Evaluation evaluation)
	{
		var finalBySubject = evaluation.FinalRatings.ToDictionary(static r => r.Subject);
		return [
			.. evaluation.Profile.ChosenALevels
						 .Select(subject => finalBySubject[subject])
						 .Where(static rating => rating.Rating == Rating.Red),
		];
	}

	private static IReadOnlyList<string> RejectedChoices(Evaluation evaluation) => [
		.. StaleChoiceRatings(evaluation)
			.Select(static rating =>
				$"chosen_a_levels entry '{EnumNames.NameOf(rating.Subject)}' is no longer available: {rating.Reason}"),
	];

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
						[.. overridesBySubject[r.Subject]]) {
						EntryEquivalentReason = EntryEquivalentReason(e.Profile, r.Subject, Catalogue),
					};
				}),
			],
			e.Summary);
	}

	// Return within the loop on the first satisfying qualification rather than testing FirstOrDefault against
	// the struct default: Qualification is a value type, so a "no match" default is only distinguishable from
	// a real all-default match by luck, and the sentinel couples correctness to which fields happen to be
	// zero. The explicit match is also the reason we cite, with no re-derivation.
	private string? EntryEquivalentReason(StudentProfile profile, Subject subject, CatalogueData catalogue)
	{
		foreach (var equivalent in catalogue.Meta(subject).EntryEquivalents) {
			foreach (var qualification in profile.PriorQualifications) {
				if (Scale.Satisfies(qualification, equivalent)) {
					return
						$"Entry equivalent satisfied by prior qualification {qualification.Subject} {EnumNames.NameOf(qualification.Type)} {qualification.Grade} for {EnumNames.NameOf(subject)}.";
				}
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
