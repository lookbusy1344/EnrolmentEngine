namespace EnrolmentRules.Domain;

/// <summary>
///     A host-code downgrade applied by the constraint pass (§1.6): the subject, its rating
///     <see cref="From" /> the engine's base verdict, the rating it was moved <see cref="To" />, and the
///     reason. Adjustments only ever downgrade (<see cref="To" /> is at least as severe as
///     <see cref="From" />), which is what keeps the pipeline monotone and single-pass. The trail of
///     adjustments is the explainable record of why a base rating was overridden (Phase 7).
/// </summary>
public sealed record Adjustment(Subject Subject, Rating From, Rating To, string Reason);

/// <summary>
///     The aggregate verdict over the final ratings (§1.6): how many subjects ended green and amber, and
///     the projected UCAS tariff — full <see cref="SubjectMeta.UcasWeight" /> for each green plus
///     <see cref="PolicyThresholds.AmberTariffFactor" /> of it for each amber. Computed after the (optional,
///     normally-disabled) green cap, so it reflects the post-cap counts when the cap is configured.
/// </summary>
public sealed record EnrolmentSummary(int GreenCount, int AmberCount, double ProjectedTariff);

/// <summary>
///     One line of the §1.7 output: a subject, its <em>final</em> traffic-light rating and the deciding
///     human reason — the engine rule's <c>SuccessEvent</c> when nothing overrode it, otherwise the
///     most-severe host-code <see cref="Adjustment" />'s reason.
/// </summary>
public sealed record Recommendation(Subject Subject, Rating Rating, string Reason);

/// <summary>
///     The whole-student verdict (§1.7): the eligibility outcome, one <see cref="Recommendation" /> per
///     subject (ranked green → amber → red, then by descending UCAS weight), the aggregate
///     <see cref="EnrolmentSummary" /> and the full host-code <see cref="Adjustment" /> trail (constraint
///     pass, plus the optional green cap when configured) that explains every downgrade. This is the
///     document the golden-file suite locks.
/// </summary>
public sealed record EnrolmentResult(
	bool Eligible,
	EquatableArray<string> EligibilityReasons,
	EquatableArray<Recommendation> Recommendations,
	EnrolmentSummary Summary,
	EquatableArray<Adjustment> Adjustments);

/// <summary>
///     The provenance for a single recommendation (Phase 7, <c>--explain</c>): the final rating plus the
///     full trail behind it — the deciding engine <see cref="Rule" /> and its <see cref="BaseRating" /> /
///     <see cref="BaseReason" />, the <see cref="PredictedPoints" /> the tier matched on, and every
///     host-code <see cref="Overrides">override</see> that moved the base verdict. An explanation is "the
///     winning rule's reason, plus any adjustment that overrode it".
/// </summary>
public sealed record Explanation(
	Subject Subject,
	Rating Rating,
	string Reason,
	Rating BaseRating,
	string Rule,
	string BaseReason,
	double PredictedPoints,
	EquatableArray<Adjustment> Overrides)
{
	/// <summary>Optional note describing the prior qualification that satisfied entry.</summary>
	public string? EntryEquivalentReason { get; init; }
}

/// <summary>
///     The explained whole-student verdict (<c>--explain</c>): the eligibility outcome and one
///     <see cref="Explanation" /> per subject (same ranking as <see cref="EnrolmentResult" />), carrying
///     the per-recommendation provenance the plain result omits.
/// </summary>
public sealed record ExplainedResult(
	bool Eligible,
	EquatableArray<string> EligibilityReasons,
	EquatableArray<Explanation> Explanations,
	EnrolmentSummary Summary);

/// <summary>
///     A single counterfactual grade change the advisor proposes: a GCSE subject and the grade move
///     from the student's current value to the suggested one.
/// </summary>
public sealed record GradeChange(string GcseSubject, int From, int To);

/// <summary>
///     The advisor's result for one amber/red subject: the current rating, the target rating the search
///     is aiming for, the exact grade changes that achieve it when reachable, and a reason when it is not
///     reachable within budget.
/// </summary>
public sealed record SubjectAdvice(
	Subject Subject,
	Rating Current,
	Rating Target,
	EquatableArray<GradeChange> Changes,
	bool Reachable,
	string? BlockedReason);

/// <summary>
///     The minimal bundle of GCSE grade changes that clears the eligibility gate for an ineligible
///     student. Present only when <see cref="AdviceResult.Eligible" /> is <c>false</c>; the per-subject
///     <see cref="AdviceResult.Advice" /> is empty in that case, since no subject tiers are reached behind
///     a closed gate.
/// </summary>
public sealed record GateAdvice(EquatableArray<GradeChange> Changes);

/// <summary>
///     The counterfactual advisor output. For an eligible student, <see cref="Advice" /> carries one
///     <see cref="SubjectAdvice" /> per amber/red subject and <see cref="Gate" /> is <c>null</c>. For an
///     ineligible student, <see cref="Advice" /> is empty and <see cref="Gate" /> carries the
///     gate-clearing bundle alongside the <see cref="EligibilityReasons" />.
/// </summary>
public sealed record AdviceResult(
	bool Eligible,
	EquatableArray<string> EligibilityReasons,
	EquatableArray<SubjectAdvice> Advice,
	GateAdvice? Gate);

/// <summary>
///     One line of <c>--batch</c> JSONL output (Phase 8): the student <see cref="Id" /> and exactly one of
///     a successful <see cref="Result" /> or an <see cref="Error" /> message (a parse or validation
///     failure on that line). Carrying the id keeps a line self-identifying even though the batch preserves
///     input order, and isolating per-line failures means one bad student never aborts the run.
/// </summary>
public sealed record BatchOutcome(string Id, EnrolmentResult? Result, string? Error);
