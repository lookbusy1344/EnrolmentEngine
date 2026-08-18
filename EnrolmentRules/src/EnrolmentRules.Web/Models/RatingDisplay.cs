namespace EnrolmentRules.Web.Models;

using Domain;

/// <summary>The single place a <see cref="Rating" /> is mapped to a CSS class, so no colour-only signal appears twice.</summary>
public static class RatingDisplay
{
	/// <summary>The pill a choice wears when it is simply accepted — deliberately not a rating colour.</summary>
	private const string ChosenCssClass = "text-bg-primary";

	public static string CssClass(Rating rating) => rating switch {
		Rating.Green => "text-bg-success",
		Rating.Amber => "text-bg-warning",
		Rating.Red => "text-bg-danger",
		_ => throw new ArgumentOutOfRangeException(nameof(rating), rating, "Unknown rating."),
	};

	/// <summary>
	///     The pill class a committed choice wears in the basket. An amber choice keeps its amber pill — it is
	///     borderline and would need additional authorisation before enrolment — while anything else reads as a
	///     plain accepted choice. <paramref name="rating" /> is null when the snapshot yields no per-subject
	///     ratings at all (invalid facts, or the eligibility gate failed).
	/// </summary>
	public static string BasketCssClass(Rating? rating) => IsBorderline(rating) ? CssClass(Rating.Amber) : ChosenCssClass;

	/// <summary>Whether a committed choice needs additional authorisation before it can be enrolled.</summary>
	public static bool IsBorderline(Rating? rating) => rating == Rating.Amber;

	/// <summary>
	///     "What's open to you" card order: colour first (green, amber, red — <see cref="Rating" />'s own
	///     ascending-severity declaration order), then alphabetically by the prettified subject label. Display
	///     only — distinct from <c>Aggregator.Rank</c>, which ties break by descending priority weight for the
	///     shortlist/recommendation ordering instead.
	/// </summary>
	public static IOrderedEnumerable<Explanation> OrderForCards(IEnumerable<Explanation> explanations) =>
		explanations
			.OrderBy(static e => e.Rating)
			.ThenBy(static e => TextFormatting.Prettify(e.Subject.Value), StringComparer.Ordinal);
}
