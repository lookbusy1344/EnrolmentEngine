namespace EnrolmentRules.Domain;

/// <summary>
///     The policy object exposed to RulesEngine lambdas. It wraps the loaded
///     <see cref="PolicyThresholds" /> in a member surface that can be safely read from workflow
///     expressions. Only the knobs the workflows actually read are exposed — the optional green cap
///     (<see cref="PolicyThresholds.MaxGreenChoices" />) and amber score factor
///     (<see cref="PolicyThresholds.AmberScoreFactor" />) are host-code aggregation knobs the lambdas
///     never see, so they stay on <see cref="PolicyThresholds" /> and are not mirrored here.
/// </summary>
public sealed class PolicyFacts(PolicyThresholds thresholds)
{
	public int PassGrade { get; } = thresholds.PassGrade;

	public int MinPasses { get; } = thresholds.MinPasses;

	public int TopEntry { get; } = thresholds.TopEntry;

	public int StrongEntry { get; } = thresholds.StrongEntry;

	public int StandardEntry { get; } = thresholds.StandardEntry;

	public double FurtherMathsAverageEntry { get; } = thresholds.FurtherMathsAverageEntry;

	public double HumanitiesAverageEntry { get; } = thresholds.HumanitiesAverageEntry;

	public double MinDfeGreenProbabilityAtOrAbove { get; } = thresholds.MinDfeGreenProbabilityAtOrAbove;

	public double MinDfeAmberProbabilityAtOrAbove { get; } = thresholds.MinDfeAmberProbabilityAtOrAbove;

	public int AdultAge { get; } = thresholds.AdultAge;
}
