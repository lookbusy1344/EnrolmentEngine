namespace EnrolmentRules.Domain.RuntimeBinding;

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

	public int StandardEntry { get; } = thresholds.StandardEntry;

	/// <summary>
	///     The exceptional GCSE bar (a top grade) used as a hard entry gate for the most demanding
	///     subjects — Maths and Physics require Maths at this level regardless of the regression tiers.
	/// </summary>
	public int ExceptionalEntry { get; } = thresholds.ExceptionalEntry;

	public double MinDfeGreenProbabilityAtOrAbove { get; } = thresholds.MinDfeGreenProbabilityAtOrAbove;

	public double MinDfeAmberProbabilityAtOrAbove { get; } = thresholds.MinDfeAmberProbabilityAtOrAbove;

	/// <summary>
	///     The top-N GCSE eligibility knobs (§ top-N aggregate facts). Zero/0.0 when unset — a policy
	///     that does not configure these never authors an eligibility rule that reads them, so the
	///     default is unreachable rather than a real "pass automatically" value.
	/// </summary>
	public int BestGcseCount { get; } = thresholds.BestGcseCount.GetValueOrDefault();

	public int MinBestGcsePoints { get; } = thresholds.MinBestGcsePoints.GetValueOrDefault();

	public int TopGcseAverageCount { get; } = thresholds.TopGcseAverageCount.GetValueOrDefault();

	public double MinTopGcseAverage { get; } = thresholds.MinTopGcseAverage.GetValueOrDefault();
}
