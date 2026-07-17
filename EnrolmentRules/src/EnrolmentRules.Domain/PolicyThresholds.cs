namespace EnrolmentRules.Domain;

/// <summary>
///     The runtime-loaded policy knobs for eligibility and subject entry/rating. These values live in
///     <c>data/thresholds.yaml</c> and are schema-validated at startup so the workflows can reference
///     them as data rather than compile-time constants.
/// </summary>
/// <remarks>
///     <see cref="AccessibleAverageEntry" /> is the average-GCSE bar for the accessible subjects
///     (sociology). It is deliberately a knob of its own rather than a reuse of
///     <see cref="HumanitiesAverageEntry" />, which history, geography, politics, religious studies and
///     law also read: keeping them separate is what lets sociology sit at the eligibility minimum
///     without dragging the other five humanities down with it.
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
/// </remarks>
public sealed record PolicyThresholds(
	int PassGrade,
	int MinPasses,
	int TopEntry,
	int StrongEntry,
	int StandardEntry,
	int ExceptionalEntry,
	double FurtherMathsAverageEntry,
	double HumanitiesAverageEntry,
	double AccessibleAverageEntry,
	double MinDfeGreenProbabilityAtOrAbove,
	double MinDfeAmberProbabilityAtOrAbove,
	int AdultAge,
	int MaxChosenALevels,
	int HighAttainmentMaxChosenALevels,
	double HighAttainmentAverageGcse,
	int? MaxGreenChoices,
	double AmberScoreFactor,
	bool AdviceConsidersUnsatGcses = false,
	int AdviceMaxGradeCost = 12,
	int AdviceMaxSubjectsChanged = 3,
	int? AdviceMaxPipelineEvaluations = null);
