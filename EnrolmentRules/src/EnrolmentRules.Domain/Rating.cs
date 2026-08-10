namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     A traffic-light rating for an A-level recommendation. Declared in ascending severity
///     (<see cref="Green" /> least severe, <see cref="Red" /> most severe) so that
///     <see cref="RatingExtensions.MostSevere" /> is a simple max over the enum value.
///     Host-code adjustments only ever downgrade, i.e. move towards <see cref="Red" />.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Rating>))]
public enum Rating
{
	[JsonStringEnumMemberName("green")] Green = 0,

	[JsonStringEnumMemberName("amber")] Amber = 1,

	[JsonStringEnumMemberName("red")] Red = 2,
}

public static class RatingExtensions
{
	/// <summary>The more severe (closer to <see cref="Rating.Red" />) of two ratings.</summary>
	public static Rating MostSevere(this Rating a, Rating b) => (Rating)Math.Max((int)a, (int)b);
}
