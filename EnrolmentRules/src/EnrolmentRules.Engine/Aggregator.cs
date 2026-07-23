#pragma warning disable CS3019
namespace EnrolmentRules.Engine;

using System.Globalization;
using Domain;

/// <summary>
///     Host-code aggregation over the final ratings (§1.6): the programme priority score, ranked shortlist,
///     whole-student chosen-subject cap, and optional green choice cap. Both caps read the constraint
///     pass's result, so the pipeline has a fixed phase order (predict → engine → constraints →
///     chosen-subject cap → green cap → aggregate). All weights and caps come from the <see cref="Catalogue" />
///     and the loaded <see cref="PolicyThresholds" />; nothing here is a literal.
/// </summary>
internal static class Aggregator
{
	public const string ExceedsCapReason = "exceeds auto-enrol cap";
	public const string ExceedsChosenSubjectCapReason = "exceeds chosen subject cap";

	/// <summary>The clause marking a limit already lifted by the high-attainment threshold.</summary>
	public const string RaisedLimitNote = "already raised for a high GCSE average";

	public static IReadOnlyList<Adjustment> CapChosenSubjects(
		IReadOnlyList<SubjectRating> ratings,
		StudentProfile profile,
		PolicyThresholds thresholds)
	{
		var highAttainment = profile.AverageGcseScore >= thresholds.HighAttainmentAverageGcse;
		var cap = highAttainment ? thresholds.HighAttainmentMaxChosenALevels : thresholds.MaxChosenALevels;
		if (profile.ChosenALevels.Count < cap) {
			return [];
		}

		var reason = ChosenSubjectCapReason(profile, thresholds, cap, highAttainment);
		return [
			.. ratings
				.Where(r => r.Rating != Rating.Red && !profile.ChosenALevels.Contains(r.Subject))
				.Select(r => new Adjustment(r.Subject, r.Rating, Rating.Red, AdjustmentKind.ChosenSubjectCap, reason)),
		];
	}

	/// <summary>
	///     The self-contained explanation for a chosen-subject-cap downgrade: the block is the student's own
	///     choice count, not anything about the barred subject, so the reason has to say which limit was hit,
	///     how it was reached, and what would move it — otherwise the bare
	///     <see cref="ExceedsChosenSubjectCapReason" /> reads as a property of the subject.
	/// </summary>
	private static string ChosenSubjectCapReason(
		StudentProfile profile, PolicyThresholds thresholds, int cap, bool highAttainment)
	{
		var chosen = string.Join(", ", profile.ChosenALevels.Select(static s => EnumNames.NameOf(s)));
		var limitNote = highAttainment
			? RaisedLimitNote
			: string.Create(
				CultureInfo.InvariantCulture,
				$"rising to {thresholds.HighAttainmentMaxChosenALevels} at a GCSE average of {thresholds.HighAttainmentAverageGcse:0.0} or above, and yours is {profile.AverageGcseScore:0.0}");

		return string.Create(
			CultureInfo.InvariantCulture,
			$"{ExceedsChosenSubjectCapReason}: {profile.ChosenALevels.Count} of {cap} permitted A-level choices already made ({chosen}) — the limit is {cap} ({limitNote}). Remove a choice to free a place.");
	}

	/// <summary>
	///     The green choice cap — an <em>optional</em> feature, disabled in normal operation. When
	///     <see cref="PolicyThresholds.MaxGreenChoices" /> is <c>null</c> (the shipped default) the cap does
	///     nothing and every green stays green: the engine reports what a student is permitted to study, not
	///     an enforced optimal shortlist. When a cap <em>is</em> configured and more than that many subjects
	///     are green, demote the lowest-weight surplus greens to amber. Returns one <see cref="Adjustment" />
	///     per demoted subject (empty when disabled or at/below the cap), applied via
	///     <see cref="ConstraintPass.Apply" />.
	/// </summary>
	public static IReadOnlyList<Adjustment> CapGreens(
		IReadOnlyList<SubjectRating> ratings, CatalogueData catalogue, PolicyThresholds thresholds)
	{
		if (thresholds.MaxGreenChoices is not int cap) {
			return [];
		}

		var greens = ratings
			.Where(static r => r.Rating == Rating.Green)
			.OrderBy(r => catalogue.Meta(r.Subject).PriorityWeight)
			.ThenBy(static r => r.Subject)
			.ToList();

		var surplus = greens.Count - cap;
		return surplus <= 0
			? []
			: [
				.. greens.Take(surplus)
					.Select(r => new Adjustment(
						r.Subject, Rating.Green, Rating.Amber, AdjustmentKind.Cap,
						GreenCapReason(greens.Count, cap, catalogue.Meta(r.Subject).PriorityWeight))),
			];
	}

	/// <summary>
	///     The green-cap reason. The subject was not demoted on its own merits — it stayed green through the
	///     whole pipeline and lost a ranking against its peers — so the reason has to name the cap, how far
	///     over it the student is, and the priority weight that decided the ordering.
	/// </summary>
	private static string GreenCapReason(int greenCount, int cap, int priorityWeight) =>
		string.Create(
			CultureInfo.InvariantCulture,
			$"{ExceedsCapReason}: {greenCount} subjects rated green against an auto-enrol cap of {cap}, and this subject's priority weight ({priorityWeight}) is among the {greenCount - cap} lowest — it is still open to you, but not auto-enrolled.");

	/// <summary>
	///     The aggregate summary over the final ratings: green/amber counts and the programme priority score
	///     (full weight per green, <see cref="PolicyThresholds.AmberScoreFactor" /> of it per amber).
	/// </summary>
	public static EnrolmentSummary Summarise(
		IReadOnlyList<SubjectRating> ratings, CatalogueData catalogue, PolicyThresholds thresholds)
	{
		var greenWeight = ratings.Where(static r => r.Rating == Rating.Green).Sum(r => catalogue.Meta(r.Subject).PriorityWeight);
		var amberWeight = ratings.Where(static r => r.Rating == Rating.Amber).Sum(r => catalogue.Meta(r.Subject).PriorityWeight);

		return new(
			ratings.Count(static r => r.Rating == Rating.Green),
			ratings.Count(static r => r.Rating == Rating.Amber),
			greenWeight + (thresholds.AmberScoreFactor * amberWeight));
	}

	/// <summary>
	///     The ranked shortlist: least-severe rating first (green &gt; amber &gt; red), ties broken by
	///     descending <see cref="SubjectMeta.PriorityWeight" />. Returns every subject; callers take the top-N.
	/// </summary>
	public static IReadOnlyList<SubjectRating> Rank(IReadOnlyList<SubjectRating> ratings, CatalogueData catalogue) => [
		.. ratings
			.OrderBy(static r => (int)r.Rating)
			.ThenByDescending(r => catalogue.Meta(r.Subject).PriorityWeight),
	];
}
