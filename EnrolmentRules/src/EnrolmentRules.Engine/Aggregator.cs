namespace EnrolmentRules.Engine;

using Domain;

/// <summary>
///     Host-code aggregation over the final ratings (§1.6): the optional green choice cap, the UCAS tariff
///     summary and the ranked shortlist. The cap is an opt-in downstream stage (off by default) that reads
///     the constraint pass's result, so when enabled it must run <em>after</em> <see cref="ConstraintPass" />
///     — the pipeline has a fixed phase order (predict → engine → constraints → cap/aggregate). All weights come from the
///     <see cref="Catalogue" /> and the loaded <see cref="PolicyThresholds" />; nothing here is a literal.
/// </summary>
public static class Aggregator
{
	public const string ExceedsCapReason = "exceeds auto-enrol cap";

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
		if (thresholds.MaxGreenChoices is not { } cap) {
			return [];
		}

		var greens = ratings
			.Where(static r => r.Rating == Rating.Green)
			.OrderBy(r => catalogue.Meta(r.Subject).UcasWeight)
			.ThenBy(static r => r.Subject)
			.ToList();

		var surplus = greens.Count - cap;
		return surplus <= 0
			? []
			: [.. greens.Take(surplus).Select(static r => new Adjustment(r.Subject, Rating.Green, Rating.Amber, ExceedsCapReason))];
	}

	/// <summary>
	///     The aggregate summary over the final ratings: green/amber counts and the projected UCAS tariff
	///     (full weight per green, <see cref="PolicyThresholds.AmberTariffFactor" /> of it per amber).
	/// </summary>
	public static EnrolmentSummary Summarise(
		IReadOnlyList<SubjectRating> ratings, CatalogueData catalogue, PolicyThresholds thresholds)
	{
		var greenWeight = ratings.Where(static r => r.Rating == Rating.Green).Sum(r => catalogue.Meta(r.Subject).UcasWeight);
		var amberWeight = ratings.Where(static r => r.Rating == Rating.Amber).Sum(r => catalogue.Meta(r.Subject).UcasWeight);

		return new(
			ratings.Count(static r => r.Rating == Rating.Green),
			ratings.Count(static r => r.Rating == Rating.Amber),
			greenWeight + (thresholds.AmberTariffFactor * amberWeight));
	}

	/// <summary>
	///     The ranked shortlist: least-severe rating first (green &gt; amber &gt; red), ties broken by
	///     descending <see cref="SubjectMeta.UcasWeight" />. Returns every subject; callers take the top-N.
	/// </summary>
	public static IReadOnlyList<SubjectRating> Rank(IReadOnlyList<SubjectRating> ratings) =>
		Rank(ratings, Catalogue.Current);

	public static IReadOnlyList<SubjectRating> Rank(IReadOnlyList<SubjectRating> ratings, CatalogueData catalogue) => [
		.. ratings
			.OrderBy(static r => (int)r.Rating)
			.ThenByDescending(r => catalogue.Meta(r.Subject).UcasWeight),
	];
}
