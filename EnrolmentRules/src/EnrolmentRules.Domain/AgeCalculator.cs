namespace EnrolmentRules.Domain;

/// <summary>
///     Derives a whole-years age from a date of birth as of a reference date (§1.1). Age is deliberately not
///     a stored input fact: it is not a pure function of the document, it depends on <em>when</em> the
///     student is assessed. Keeping the reference date explicit (rather than reading the wall clock) is what
///     makes the whole pipeline deterministic — the same document evaluated against the same reference date
///     always yields the same age, so golden files and property tests never rot.
/// </summary>
public static class AgeCalculator
{
	/// <summary>
	///     The student's age in whole (completed) years on <paramref name="asOf" />, birthday-aware: the year
	///     difference, decremented when the birthday has not yet occurred on or before the reference date.
	///     Clamped at zero so a reference date earlier than the birth date never yields a negative age.
	/// </summary>
	public static int WholeYears(DateOnly dateOfBirth, DateOnly asOf)
	{
		var age = asOf.Year - dateOfBirth.Year;
		if (asOf < dateOfBirth.AddYears(age)) {
			age--;
		}

		return Math.Max(0, age);
	}
}
