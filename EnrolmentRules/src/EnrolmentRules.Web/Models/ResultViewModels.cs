namespace EnrolmentRules.Web.Models;

using Domain;
using Engine;

/// <summary>
///     The result the page renders for the current session snapshot: either the validation errors
///     <see cref="Domain.StudentValidator" /> raised against the mapped <c>StudentInput</c>, or the
///     selected policy's non-destructive <see cref="PolicyComparisonResult" /> from
///     <see cref="IEnrolmentPolicyRegistry.Compare" />. Never both — an invalid snapshot shows no
///     (possibly stale) recommendations.
/// </summary>
public sealed record EnrolmentResultsViewModel(EquatableArray<string> ValidationErrors, PolicyComparisonResult? Comparison)
{
	public bool IsValid => ValidationErrors.Count == 0;

	public static EnrolmentResultsViewModel From(ValidatedEvaluation<PolicyComparisonResult> comparison)
	{
		ArgumentNullException.ThrowIfNull(comparison);
		return new(comparison.Validation.Errors, comparison.Value);
	}
}

/// <summary>
///     One committed choice as the basket renders it: the subject, its non-destructive
///     <see cref="ChoiceStatus" /> under the selected policy, and (when <see cref="ChoiceStatus.Available" />)
///     the rating this evaluation gives it. A choice is never dropped from the basket by the page itself —
///     an <see cref="ChoiceStatus.Unavailable" /> (offered, currently red) or
///     <see cref="ChoiceStatus.NotOffered" /> (absent from this policy's catalogue) choice stays visible,
///     annotated, so switching policies never silently loses a selection. An <see cref="ChoiceStatus.Available" />
///     entry rated amber is <em>borderline</em> — it would need additional authorisation before enrolment.
/// </summary>
public sealed record BasketEntry(Subject Subject, ChoiceStatus Status, Rating? Rating, string? Reason)
{
	public bool IsBorderline => Status == ChoiceStatus.Available && RatingDisplay.IsBorderline(Rating);

	public bool IsInvalid => Status is ChoiceStatus.Unavailable or ChoiceStatus.NotOffered;

	public string CssClass => Status switch {
		ChoiceStatus.Unavailable => "text-bg-danger",
		ChoiceStatus.NotOffered => "text-bg-danger",
		_ => RatingDisplay.BasketCssClass(Rating),
	};

	/// <summary>
	///     Project every <see cref="PolicyComparisonResult.ChoiceStatuses" /> entry into a basket row: valid
	///     choices (available or unrated) before invalid ones (<see cref="IsInvalid" />), each group
	///     alphabetical by its displayed label rather than choice order.
	/// </summary>
	public static IReadOnlyList<BasketEntry> From(PolicyComparisonResult? comparison)
	{
		if (comparison is null) {
			return [];
		}

		var ratings = comparison.Explanation.Explanations.ToDictionary(static e => e.Subject, static e => e.Rating);
		return [
			.. comparison.ChoiceStatuses
							  .Select(status =>
								  new BasketEntry(
									  status.Subject,
									  status.Status,
									  ratings.TryGetValue(status.Subject, out var rating) ? rating : null,
									  status.Reason))
							  .OrderBy(static entry => entry.IsInvalid)
							  .ThenBy(static entry => TextFormatting.Prettify(entry.Subject.Value), StringComparer.Ordinal),
		];
	}
}
