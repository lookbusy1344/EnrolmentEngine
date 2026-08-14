namespace EnrolmentRules.Web.Models;

using Domain;

/// <summary>
///     The result the page renders for the current session snapshot: either the validation errors
///     <see cref="Domain.StudentValidator" /> raised against the mapped <c>StudentInput</c>, or the full
///     per-subject <see cref="ExplainedResult" /> from <c>ExplainValidated</c>. Never both — an invalid snapshot
///     shows no (possibly stale) recommendations.
/// </summary>
public sealed record EnrolmentResultsViewModel(EquatableArray<string> ValidationErrors, ExplainedResult? Result)
{
	public bool IsValid => ValidationErrors.Count == 0;

	public static EnrolmentResultsViewModel From(ValidatedEvaluation<ExplainedResult> evaluation)
	{
		ArgumentNullException.ThrowIfNull(evaluation);
		return new(evaluation.Validation.Errors, evaluation.Value);
	}
}

/// <summary>
///     One committed choice as the basket renders it: the subject plus the rating this evaluation gives it.
///     A choice is only ever green or amber (a red one is ejected before the page renders), and an amber one
///     is <em>borderline</em> — it sits in the basket but would need additional authorisation before enrolment.
///     <paramref name="Rating" /> is null when the snapshot produced no per-subject ratings at all — invalid
///     facts, or the eligibility gate failed — in which case the basket falls back to a plain pill.
/// </summary>
public sealed record BasketEntry(Subject Subject, Rating? Rating)
{
	public bool IsBorderline => RatingDisplay.IsBorderline(Rating);

	public string CssClass => RatingDisplay.BasketCssClass(Rating);

	/// <summary>Pairs each committed choice with its rating from <paramref name="results" />, in basket order.</summary>
	public static IReadOnlyList<BasketEntry> From(IReadOnlyList<Subject> chosen, EnrolmentResultsViewModel? results)
	{
		ArgumentNullException.ThrowIfNull(chosen);
		var ratings = results?.Result?.Explanations.ToDictionary(e => e.Subject, e => e.Rating);
		return [.. chosen.Select(subject => new BasketEntry(subject, Lookup(ratings, subject)))];
	}

	private static Rating? Lookup(Dictionary<Subject, Rating>? ratings, Subject subject) =>
		ratings is not null && ratings.TryGetValue(subject, out var rating) ? rating : null;
}
