namespace EnrolmentRules.Web.Models;

using Domain;

/// <summary>The single place a <see cref="Rating" /> is mapped to a CSS class, so no colour-only signal appears twice.</summary>
public static class RatingDisplay
{
	public static string CssClass(Rating rating) => rating switch {
		Rating.Green => "text-bg-success",
		Rating.Amber => "text-bg-warning",
		Rating.Red => "text-bg-danger",
		_ => throw new ArgumentOutOfRangeException(nameof(rating), rating, "Unknown rating."),
	};
}
