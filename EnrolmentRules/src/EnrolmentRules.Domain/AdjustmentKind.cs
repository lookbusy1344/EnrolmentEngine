namespace EnrolmentRules.Domain;

/// <summary>
///     Which relationship type produced an <see cref="Adjustment" />. Members are declared in ascending
///     tie-break precedence: when two equally-severe adjustments target the same subject, the one with the
///     higher-valued kind supplies the surviving reason (the <c>ConstraintPass.Apply</c> fold). This is the
///     typed discriminator the fold reads, replacing the former recovery of precedence by matching the
///     human-readable reason text — display wording and control flow are no longer coupled, so rewording a
///     reason can never silently change which adjustment wins a tie, and a new relationship type must pick an
///     explicit precedence slot here rather than falling through to the lowest.
/// </summary>
public enum AdjustmentKind
{
	/// <summary>
	///     The optional green choice cap (<c>Aggregator.CapGreens</c>). It is applied in its own stage, after
	///     the constraint pass, so it never competes with a constraint adjustment inside a single fold; it
	///     carries the lowest precedence to mirror the former unmatched-reason default.
	/// </summary>
	Cap = 0,

	/// <summary>An unmet own-time practice requirement (amber).</summary>
	OwnTime = 1,

	/// <summary>An unmet prerequisite group (amber or red).</summary>
	Prerequisite = 2,

	/// <summary>An exclusion edge activated by a committed A-level choice (amber or red).</summary>
	ChosenSubjectExclusion = 3,

	/// <summary>The whole-student committed-choice cap: once full, further unchosen subjects are red.</summary>
	ChosenSubjectCap = 4,

	/// <summary>An incompatible activity barring the subject outright (red).</summary>
	Veto = 5,

	/// <summary>A barred prior qualification in the same subject (amber or red).</summary>
	RestudyBar = 6,
}
