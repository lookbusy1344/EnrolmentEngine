namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     A traffic-light rating for an A-level recommendation. Declared in ascending severity
///     (<see cref="Green" /> least severe, <see cref="Red" /> most severe), so the most severe of two is a
///     simple max over the enum value. Host-code adjustments only ever downgrade, i.e. move towards
///     <see cref="Red" />.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Rating>))]
public enum Rating
{
	[JsonStringEnumMemberName("green")] Green = 0,

	[JsonStringEnumMemberName("amber")] Amber = 1,

	[JsonStringEnumMemberName("red")] Red = 2,
}
