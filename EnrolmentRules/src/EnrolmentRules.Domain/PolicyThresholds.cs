namespace EnrolmentRules.Domain;

/// <summary>
///     The runtime-loaded policy knobs for eligibility and subject entry/rating. These values live in
///     <c>data/thresholds.yaml</c> and are schema-validated at startup so the workflows can reference
///     them as data rather than compile-time constants.
/// </summary>
/// <remarks>
///     Only broad-brush, widely-shared bands live here as named knobs. One-off, course-specific
///     average-GCSE bars (Further Maths, the humanities, the accessible tier) are written as literals
///     directly in the rule expressions in <c>workflows/subject-ratings.yaml</c>.
///     <see cref="MaxChosenALevels" />, <see cref="HighAttainmentMaxChosenALevels" />, and
///     <see cref="HighAttainmentAverageGcse" /> drive the selected-programme cap in host code: most
///     students may choose up to <see cref="MaxChosenALevels" />, while students at or above
///     <see cref="HighAttainmentAverageGcse" /> may choose up to
///     <see cref="HighAttainmentMaxChosenALevels" />.
///     <para>
///         <see cref="MaxGreenChoices" /> is optional and normally unset: when it is <c>null</c> the green
///         cap is disabled and every green stays green. A positive value opts the cap in (see
///         <c>Aggregator.CapGreens</c>).
///     </para>
///     <para>
///         <see cref="AdviceConsidersUnsatGcses" /> is a diagnostic knob, off by default: when
///         <c>false</c> the counterfactual advisor only proposes raising GCSEs the student already sat;
///         when <c>true</c> it reverts to the old, much heavier search that may also propose sitting a
///         brand-new GCSE. The separate gate-clearing fallback for an ineligible student may still
///         propose brand-new GCSEs even when this knob is off, because no grade bump can clear the gate
///         without enough passes. Retained for diagnosing why a subject is reachable/unreachable, not
///         for normal operation.
///     </para>
///     <para>
///         <see cref="BestGcseCount" />, <see cref="MinBestGcsePoints" />, <see cref="TopGcseAverageCount" />
///         and <see cref="MinTopGcseAverage" /> are the top-N GCSE eligibility knobs an auxiliary policy
///         (e.g. Elite's "best eight total at least 60" and "top seven average at least 7.0") binds a
///         <c>lookup.BestTotal(...)</c>/<c>lookup.BestAverage(...)</c> eligibility rule to. All four are
///         optional and validate all-or-none: the shipped Standard policy leaves them unset, and no
///         top-N eligibility rule may reference them. <see cref="MinChosenALevels" /> is independent and
///         defaults to zero so existing callers and Standard behaviour do not change; a policy that
///         requires a minimum committed-choice count for final-programme validation sets it directly.
///     </para>
/// </remarks>
public sealed record PolicyThresholds(
	int PassGrade,
	int MinPasses,
	int TopEntry,
	int StandardEntry,
	int ExceptionalEntry,
	double MinDfeGreenProbabilityAtOrAbove,
	double MinDfeAmberProbabilityAtOrAbove,
	int MaxChosenALevels,
	int HighAttainmentMaxChosenALevels,
	double HighAttainmentAverageGcse,
	int? MaxGreenChoices,
	double AmberScoreFactor,
	bool AdviceConsidersUnsatGcses = false,
	int AdviceMaxGradeCost = 12,
	int AdviceMaxSubjectsChanged = 3,
	int? AdviceMaxPipelineEvaluations = null,
	int? BestGcseCount = null,
	int? MinBestGcsePoints = null,
	int? TopGcseAverageCount = null,
	double? MinTopGcseAverage = null,
	int MinChosenALevels = 0);
