namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     How a prerequisite group may be satisfied. <see cref="Qualifying" /> (the default) accepts the
///     required subject either rating green/amber in this run <em>or</em> being a committed A-level — it is
///     enough that the subject is viable for the student. <see cref="Chosen" /> is stricter: only a committed
///     <c>chosen_a_levels</c> entry counts, so a subject that merely rates well does not satisfy it (e.g.
///     "Further Maths needs Maths to have been <em>chosen</em>, not just to be a viable option").
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrerequisiteSatisfaction>))]
public enum PrerequisiteSatisfaction
{
	[JsonStringEnumMemberName("qualifying")]
	Qualifying = 0,

	[JsonStringEnumMemberName("chosen")] Chosen = 1,
}
