namespace EnrolmentRules.Domain;

/// <summary>
///     Which GCSEs the counterfactual advisor's search may propose. It answers "may the advisor suggest
///     sitting a GCSE the student never took, or only bumping grades they already hold?" — the per-call
///     override of the loaded <see cref="PolicyThresholds.AdviceConsidersUnsatGcses" /> default. A named
///     choice rather than a bare Boolean so <c>Advise(student, UnsatGcseAdvice.IncludeUnsat)</c> reads at
///     the call site, where <c>Advise(student, true)</c> did not.
/// </summary>
public enum UnsatGcseAdvice
{
	/// <summary>
	///     Propose grade improvements only on GCSEs the student already sat. A subject gated on a GCSE they
	///     never took is then unreachable by grade changes alone, reported as that entry rule's own reason.
	///     This is the actionable, tractable default — the search space is exponential in the candidate count.
	/// </summary>
	HeldOnly = 0,

	/// <summary>
	///     Also propose sitting GCSEs the student never took — the heavier diagnostic search over every known
	///     GCSE, matching <see cref="PolicyThresholds.AdviceConsidersUnsatGcses" /> when it is on.
	/// </summary>
	IncludeUnsat = 1,
}
