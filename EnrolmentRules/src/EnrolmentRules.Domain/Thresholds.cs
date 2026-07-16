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
}
