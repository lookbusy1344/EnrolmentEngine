namespace EnrolmentRules.Domain;

/// <summary>
///     The runtime-loaded policy knobs for eligibility and subject entry/rating. These values live in
///     <c>data/thresholds.yaml</c> and are schema-validated at startup so the workflows can reference
///     them as data rather than compile-time constants.
/// </summary>
/// <remarks>
///     <see cref="MaxGreenChoices" /> is optional and normally unset: when it is <c>null</c> the green
///     cap is disabled and every green stays green. A positive value opts the cap in (see
///     <c>Aggregator.CapGreens</c>).
///     <para>
///         <see cref="AdviceConsidersUnsatGcses" /> is a diagnostic knob, off by default: when
///         <c>false</c> the counterfactual advisor only proposes raising GCSEs the student already sat;
///         when <c>true</c> it reverts to the old, much heavier search that may also propose sitting a
///         brand-new GCSE. Retained for diagnosing why a subject is reachable/unreachable, not for normal
///         operation.
///     </para>
/// </remarks>
public sealed record PolicyThresholds(
	int PassGrade,
	int MinPasses,
	int TopEntry,
	int StrongEntry,
	int StandardEntry,
	double FurtherMathsAverageEntry,
	double HumanitiesAverageEntry,
	double MinDfeGreenProbabilityAtOrAbove,
	double MinDfeAmberProbabilityAtOrAbove,
	int AdultAge,
	int? MaxGreenChoices,
	double AmberTariffFactor,
	bool AdviceConsidersUnsatGcses = false,
	int AdviceMaxGradeCost = 12,
	int AdviceMaxSubjectsChanged = 3,
	int? AdviceMaxPipelineEvaluations = null);
