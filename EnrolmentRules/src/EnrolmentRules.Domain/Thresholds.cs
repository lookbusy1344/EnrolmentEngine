namespace EnrolmentRules.Domain;

/// <summary>
///     The two GCSE-scale invariants. These are not policy — they define the 1–9 grade scale itself, so
///     they stay compiled-in and are reused to validate the loaded policy values
///     (see <see cref="PolicyThresholdsStore" />). Every tunable policy knob (pass grade, entry
///     thresholds, the selected-programme cap, the optional green cap, the amber score factor, …) lives in <c>data/thresholds.yaml</c> and is
///     threaded through as <see cref="PolicyThresholds" /> — no compiled policy constant exists for them.
/// </summary>
public static class Thresholds
{
	/// <summary>Lowest GCSE grade on the 1–9 scale.</summary>
	public const int MinGcseGrade = 1;

	/// <summary>Highest GCSE grade on the 1–9 scale.</summary>
	public const int MaxGcseGrade = 9;

	/// <summary>
	///     Fit a raw typed grade onto the 1–9 integer scale: round to the nearest whole grade, then clamp to
	///     [<see cref="MinGcseGrade" />, <see cref="MaxGcseGrade" />]. Rounding happens first so a value like
	///     9.6 lands on 9 rather than rounding off the scale.
	///     <para>
	///         This is an <em>input</em> convenience for the web front-ends, not a relaxation of the boundary:
	///         <see cref="StudentValidator" /> still rejects an out-of-range grade, so the CLI and the evaluate
	///         endpoint keep failing fast on one rather than silently scoring a different student.
	///     </para>
	///     <para>
	///         Halves round away from zero to match JavaScript's <c>Math.round</c>, which the Vue mirror in
	///         <c>ClientApp/src/state/gcseGrade.ts</c> uses — the two must agree on 6.5 ⇒ 7.
	///     </para>
	/// </summary>
	public static int NormalizeGcseGrade(double raw) =>
		Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), MinGcseGrade, MaxGcseGrade);
}
