namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Phase 6 — host-code aggregation over the <em>final</em> ratings: the programme priority score summary, the green
///     choice cap (§1.6), and the ranked shortlist. The score and cap are cross-checked against an
///     independent recomputation from the <see cref="Catalogue" /> weights — never a literal — so a wrong
///     weight or factor breaks a test. The cap runs <em>after</em> the constraint pass: it counts greens
///     that survived the exclusion/prereq/own-time downgrades.
/// </summary>
public sealed class AggregationTests
{
	// The green cap is an optional feature, disabled in the shipped thresholds (MaxGreenChoices is null).
	// These tests opt it in explicitly to exercise the capping behaviour.
	private const int Cap = 4;
	private static readonly PolicyThresholds CappedThresholds = Harness.Thresholds with { MaxGreenChoices = Cap };

	private static SubjectRating[] Ratings(params (Subject Subject, Rating Rating)[] overrides)
	{
		var map = Catalogue.Subjects.ToDictionary(static s => s, static _ => Rating.Red);
		foreach (var (subject, rating) in overrides) {
			map[subject] = rating;
		}

		return [.. map.Select(static kv => new SubjectRating(kv.Key, kv.Value, "base"))];
	}

	private static StudentInput StrongStudent() =>
		new("S-CHOICE-CLASH", new Dictionary<string, int> {
			["english_language"] = 6,
			["maths"] = 6,
			["physics"] = 6,
			["chemistry"] = 6,
			["biology"] = 6,
			["english_literature"] = 6,
			["french"] = 6,
			["german"] = 6,
			["physical_education"] = 6,
			["computer_studies"] = 6,
			["history"] = 6,
			["music"] = 6,
			["art"] = 6,
		}, []);

	private static int Weight(Subject subject) => Catalogue.Meta(subject).PriorityWeight;

	[Fact]
	public void programme_priority_score_is_full_weight_for_greens_plus_half_for_ambers()
	{
		var ratings = Ratings(
			(Subject.Maths, Rating.Green), (Subject.Physics, Rating.Green), (Subject.Chemistry, Rating.Green),
			(Subject.Biology, Rating.Amber), (Subject.Art, Rating.Amber));

		var summary = Aggregator.Summarise(ratings, Catalogue.Default, Harness.Thresholds);

		// Independent recomputation from the catalogue weights and the loaded amber factor.
		var expected = Weight(Subject.Maths) + Weight(Subject.Physics) + Weight(Subject.Chemistry)
					   + (Harness.Thresholds.AmberScoreFactor * (Weight(Subject.Biology) + Weight(Subject.Art)));

		summary.GreenCount.Should().Be(3);
		summary.AmberCount.Should().Be(2);
		summary.ProgrammePriorityScore.Should().Be(expected);
	}

	[Fact]
	public void all_red_student_has_zero_programme_score_and_a_well_formed_shortlist()
	{
		var ratings = Ratings();

		var summary = Aggregator.Summarise(ratings, Catalogue.Default, Harness.Thresholds);
		var shortlist = Aggregator.Rank(ratings, Harness.Catalogue);

		summary.Should().Be(new EnrolmentSummary(0, 0, 0.0));
		shortlist.Should().HaveCount(Catalogue.Subjects.Count);
		shortlist.Select(r => r.Subject).Should().BeEquivalentTo(Catalogue.Subjects);
	}

	[Fact]
	public void green_cap_downgrades_the_lowest_weight_surplus_greens_first()
	{
		// One green over the cap: the single lowest-weight green is demoted, the rest stay green.
		var greens = new[] { Subject.Maths, Subject.Physics, Subject.Chemistry, Subject.Biology, Subject.EnglishLiterature };
		greens.Should().HaveCount(Cap + 1);
		var lowest = greens.OrderBy(Weight).First();

		var adjustments = Aggregator.CapGreens(Ratings([.. greens.Select(s => (s, Rating.Green))]), Catalogue.Default, CappedThresholds);

		var capped = adjustments.Should().ContainSingle().Which;
		capped.Subject.Should().Be(lowest);
		capped.From.Should().Be(Rating.Green);
		capped.To.Should().Be(Rating.Amber);
		capped.Reason.Should().Be(Aggregator.ExceedsCapReason);
	}

	[Fact]
	public void green_cap_demotes_exactly_the_surplus_count()
	{
		// Two greens over the cap ⇒ the two lowest-weight greens are demoted.
		var greens = new[] { Subject.Maths, Subject.FurtherMaths, Subject.Physics, Subject.Chemistry, Subject.Biology, Subject.History };
		var surplus = greens.Length - Cap;
		var expected = greens.OrderBy(Weight).Take(surplus).ToHashSet();

		var adjustments = Aggregator.CapGreens(Ratings([.. greens.Select(s => (s, Rating.Green))]), Catalogue.Default, CappedThresholds);

		adjustments.Should().HaveCount(surplus);
		adjustments.Select(a => a.Subject).Should().BeEquivalentTo(expected);
		adjustments.Should().OnlyContain(a => a.To == Rating.Amber);
	}

	[Fact]
	public void green_cap_is_a_no_op_at_or_below_the_cap()
	{
		var greens = Catalogue.Subjects.Take(Cap).Select(s => (s, Rating.Green));

		Aggregator.CapGreens(Ratings([.. greens]), Catalogue.Default, CappedThresholds).Should().BeEmpty();
	}

	[Fact]
	public void green_cap_disabled_by_default_keeps_every_green_green()
	{
		// The shipped thresholds leave MaxGreenChoices null: the cap never fires, no matter how many greens.
		Harness.Thresholds.MaxGreenChoices.Should().BeNull();
		var greens = Catalogue.Subjects.Select(s => (s, Rating.Green));

		Aggregator.CapGreens(Ratings([.. greens]), Catalogue.Default, Harness.Thresholds).Should().BeEmpty();
	}

	[Fact]
	public void shortlist_ranks_greens_above_ambers_above_reds_then_by_weight()
	{
		var ratings = Ratings(
			(Subject.Music, Rating.Green), (Subject.Maths, Rating.Green),
			(Subject.Art, Rating.Amber), (Subject.History, Rating.Amber));

		var ranked = Aggregator.Rank(ratings, Harness.Catalogue);

		// Greens first (Maths outranks the lower-weight Music), then ambers (History outranks Art),
		// then reds — all in descending weight within a rating band.
		ranked[0].Subject.Should().Be(Subject.Maths);
		ranked[1].Subject.Should().Be(Subject.Music);
		ranked[2].Subject.Should().Be(Subject.History);
		ranked[3].Subject.Should().Be(Subject.Art);
		ranked.Skip(4).Should().OnlyContain(r => r.Rating == Rating.Red);

		// Monotonic non-increasing by (rating-severity, then weight) across the whole list.
		ranked.Zip(ranked.Skip(1)).Should().OnlyContain(p =>
			(int)p.First.Rating < (int)p.Second.Rating
			|| ((int)p.First.Rating == (int)p.Second.Rating
				&& Weight(p.First.Subject) >= Weight(p.Second.Subject)));
	}

	[Fact]
	public void green_cap_honours_a_loaded_max_green_choices_override()
	{
		// Enabling the cap via loaded data (no rebuild) must change the demotion: with the shipped default
		// (disabled) four greens demote none; setting a cap of three demotes the lowest-weight one.
		var capped = Harness.Thresholds with { MaxGreenChoices = 3 };
		var greens = new[] { Subject.Maths, Subject.Physics, Subject.Chemistry, Subject.Biology };
		var ratings = Ratings([.. greens.Select(s => (s, Rating.Green))]);

		Aggregator.CapGreens(ratings, Catalogue.Default, Harness.Thresholds).Should().BeEmpty();
		Aggregator.CapGreens(ratings, Catalogue.Default, capped)
			.Should().ContainSingle().Which.Subject.Should().Be(greens.OrderBy(Weight).First());
	}

	[Fact]
	public void programme_priority_score_honours_a_loaded_amber_factor_override()
	{
		// The amber score factor is loaded data: changing it must change the programme priority score.
		var halved = Harness.Thresholds with { AmberScoreFactor = Harness.Thresholds.AmberScoreFactor / 2 };
		var ratings = Ratings((Subject.Maths, Rating.Green), (Subject.Art, Rating.Amber));

		var summary = Aggregator.Summarise(ratings, Catalogue.Default, halved);

		summary.ProgrammePriorityScore.Should().Be(Weight(Subject.Maths) + (halved.AmberScoreFactor * Weight(Subject.Art)));
	}

	[Fact]
	public void green_cap_counts_greens_after_the_exclusion_downgrade()
	{
		// Six greens including the History/Art exclusion pair, cap enabled at four.
		var sixGreens = Ratings(
			(Subject.Maths, Rating.Green), (Subject.Physics, Rating.Green), (Subject.Chemistry, Rating.Green),
			(Subject.Biology, Rating.Green), (Subject.History, Rating.Green), (Subject.Art, Rating.Green));

		// Constraint pass first: the exclusion demotes the lower-weight pair member (Art) to amber.
		var afterConstraints = ConstraintPass.Apply(sixGreens, ConstraintPass.Evaluate(sixGreens, new("S", 7.0, [], [], []), Harness.Catalogue));

		var capOnConstrained = Aggregator.CapGreens(afterConstraints, Catalogue.Default, CappedThresholds);
		var capOnBase = Aggregator.CapGreens(sixGreens, Catalogue.Default, CappedThresholds);

		// Run in the correct order, the cap sees five greens (one surplus) and touches only History.
		// Run on the un-constrained base it would see six greens (two surplus) — a different result.
		// This pins the cap-after-exclusion dependency.
		capOnConstrained.Should().ContainSingle().Which.Subject.Should().Be(Subject.History);
		capOnBase.Should().HaveCount(2);
	}

	[Fact]
	public void french_and_german_both_chosen_are_red_with_mutual_exclusion_winning_for_german()
	{
		var engine = Harness.ShippedEngine();
		var student = StrongStudent() with { ChosenALevels = [Subject.French, Subject.German] };

		var result = engine.Evaluate(student);

		result.Eligible.Should().BeTrue();
		result.Recommendations.Should().ContainSingle(r => r.Subject == Subject.French && r.Rating == Rating.Red);
		result.Recommendations.Should().ContainSingle(r => r.Subject == Subject.German && r.Rating == Rating.Red);

		var french = result.Recommendations.Single(r => r.Subject == Subject.French);
		var german = result.Recommendations.Single(r => r.Subject == Subject.German);

		french.Reason.Should().Be($"Cannot be combined with chosen {EnumNames.NameOf(Subject.German)}");
		german.Reason.Should().Be($"Mutual exclusion with {EnumNames.NameOf(Subject.French)} — not permitted");

		var choiceAdjustments = result.Adjustments
			.Where(a => a.Subject == Subject.French || a.Subject == Subject.German)
			.ToArray();
		choiceAdjustments.Should().HaveCount(3);
		choiceAdjustments.Should().Contain(a =>
			a.Subject == Subject.French
			&& a.From == Rating.Amber
			&& a.To == Rating.Red
			&& a.Reason == $"Cannot be combined with chosen {EnumNames.NameOf(Subject.German)}");
		choiceAdjustments.Should().Contain(a =>
			a.Subject == Subject.German
			&& a.From == Rating.Amber
			&& a.To == Rating.Red
			&& a.Reason == $"Cannot be combined with chosen {EnumNames.NameOf(Subject.French)}");
		choiceAdjustments.Should().Contain(a =>
			a.Subject == Subject.German
			&& a.From == Rating.Amber
			&& a.To == Rating.Red
			&& a.Reason == $"Mutual exclusion with {EnumNames.NameOf(Subject.French)} — not permitted");
	}
}
