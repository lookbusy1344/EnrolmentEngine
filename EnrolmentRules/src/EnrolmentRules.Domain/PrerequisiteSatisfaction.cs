namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     How a prerequisite group may be satisfied. <see cref="Qualifying" /> (the default) accepts the
///     required subject rating green/amber after its constraints. <see cref="Chosen" /> also requires a
///     committed <c>chosen_a_levels</c> entry. A red subject satisfies neither mode; a matching held prior
///     qualification supplies a separate path through the dependent subject's entry equivalents.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrerequisiteSatisfaction>))]
public enum PrerequisiteSatisfaction
{
	[JsonStringEnumMemberName("qualifying")]
	Qualifying = 0,

	[JsonStringEnumMemberName("chosen")] Chosen = 1,
}
