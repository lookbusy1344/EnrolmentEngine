namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;

/// <summary>
///     Integration-style tests for the cross-subject constraint pass, driving
///     <see cref="ConstraintPass.Evaluate" /> over the shipped catalogue. Fixtures construct the base
///     ratings directly — the constraint pass is host code, not a rule, so this is its natural unit
///     boundary. These sit alongside the <see cref="ConstraintPassTests.Apply" /> unit tests.
/// </summary>
public sealed class ConstraintPassIntegrationTests
{
	// Build a full base-rating set: every subject red unless explicitly overridden. Defaulting to red
	// isolates one constraint at a time (no stray both-green exclusion or unmet own-time leaking in).
	private static SubjectRating[] Ratings(params (Subject Subject, Rating Rating)[] overrides)
	{
		var map = Catalogue.Subjects.ToDictionary(static s => s, static _ => Rating.Red);
		foreach (var (subject, rating) in overrides) {
			map[subject] = rating;
		}

		return [.. map.Select(static kv => new SubjectRating(kv.Key, kv.Value, "base"))];
	}

	private static StudentProfile Profile(params string[] hobbies) => new("S", 7.0, [], [], hobbies);

	private static Rating Of(IEnumerable<SubjectRating> ratings, Subject subject) =>
		ratings.Single(r => r.Subject == subject).Rating;

	[Fact]
	public void further_maths_is_forced_red_when_maths_does_not_qualify()
	{
		// Further Maths cleared its own tier (green) but Maths is red: the prerequisite is unmet.
		var adjustments = ConstraintPass.Evaluate(Ratings((Subject.FurtherMaths, Rating.Green)), Profile(), Harness.Catalogue);

		var fm = adjustments.Should().ContainSingle().Which;
		fm.Subject.Should().Be(Subject.FurtherMaths);
		fm.From.Should().Be(Rating.Green);
		fm.To.Should().Be(Rating.Red);
		fm.Reason.Should().Be(ConstraintPass.MathsPrerequisiteReason);
	}

	[Fact]
	public void further_maths_is_forced_red_when_maths_qualifies_but_is_not_chosen()
	{
		// Shipped policy requires Maths to be a committed choice (requires: chosen): a merely-qualifying
		// Maths no longer satisfies Further Maths's prerequisite.
		var adjustments = ConstraintPass.Evaluate(
			Ratings((Subject.Maths, Rating.Amber), (Subject.FurtherMaths, Rating.Green)), Profile(), Harness.Catalogue);

		adjustments.Should().ContainSingle(a => a.Subject == Subject.FurtherMaths)
			.Which.To.Should().Be(Rating.Red);
	}

	[Fact]
	public void further_maths_prerequisite_is_met_by_a_committed_maths_choice()
	{
		// Maths is red in this run's ratings, but the student has already committed to Maths as an
		// A-level — a committed prerequisite is at least as strong as a qualifying one, so Further Maths
		// keeps the green its own entry table earned.
		var profile = Profile() with { ChosenALevels = [Subject.Maths] };
		var adjustments = ConstraintPass.Evaluate(Ratings((Subject.FurtherMaths, Rating.Green)), profile, Harness.Catalogue);

		adjustments.Should().NotContain(a => a.Subject == Subject.FurtherMaths);
	}

	[Fact]
	public void further_maths_is_forced_red_when_a_different_subject_is_committed()
	{
		// A committed A-level that isn't Maths does not satisfy Further Maths's prerequisite.
		var profile = Profile() with { ChosenALevels = [Subject.Physics] };
		var adjustments = ConstraintPass.Evaluate(Ratings((Subject.FurtherMaths, Rating.Green)), profile, Harness.Catalogue);

		adjustments.Should().ContainSingle(a => a.Subject == Subject.FurtherMaths)
			.Which.To.Should().Be(Rating.Red);
	}

	[Fact]
	public void a_prerequisite_group_is_met_when_any_one_alternative_is_available()
	{
		// any_of [Maths, Physics]: Physics alone satisfies the group, so nothing is emitted.
		var group = new Prerequisite([Subject.Maths, Subject.Physics], Rating.Red);

		var adjustments = ConstraintPass.PrerequisiteAdjustments(
			Subject.FurtherMaths, Rating.Green, [group], (required, _) => required == Subject.Physics);

		adjustments.Should().BeEmpty();
	}

	[Fact]
	public void an_unmet_or_group_names_every_alternative_in_its_reason()
	{
		var group = new Prerequisite([Subject.Maths, Subject.Physics], Rating.Red);

		var adjustment = ConstraintPass.PrerequisiteAdjustments(
			Subject.FurtherMaths, Rating.Green, [group], static (_, _) => false).Should().ContainSingle().Which;

		adjustment.To.Should().Be(Rating.Red);
		adjustment.Reason.Should().Be("maths or physics prerequisite not met");
	}

	[Fact]
	public void an_amber_severity_prerequisite_downgrades_to_amber_not_red()
	{
		// A soft (advisory) prerequisite: unmet, it only demotes a green to amber.
		var group = new Prerequisite([Subject.Maths], Rating.Amber);

		var adjustment = ConstraintPass.PrerequisiteAdjustments(
			Subject.Biology, Rating.Green, [group], static (_, _) => false).Should().ContainSingle().Which;

		adjustment.To.Should().Be(Rating.Amber);
	}

	[Fact]
	public void each_unmet_group_emits_so_apply_resolves_to_the_most_severe()
	{
		// Two groups on one subject (AND): an amber advisory and a hard red, both unmet.
		Prerequisite[] groups = [
			new([Subject.Maths], Rating.Amber),
			new([Subject.Physics], Rating.Red),
		];

		var adjustments = ConstraintPass.PrerequisiteAdjustments(
			Subject.Chemistry, Rating.Green, groups, static (_, _) => false).ToList();

		adjustments.Select(a => a.To).Should().BeEquivalentTo([Rating.Amber, Rating.Red]);
	}

	[Fact]
	public void a_chosen_only_group_passes_its_mode_to_the_oracle()
	{
		// The helper must consult each group under its own Requires mode, not a fixed one.
		var group = new Prerequisite([Subject.Maths], Rating.Red, PrerequisiteSatisfaction.Chosen);

		var adjustments = ConstraintPass.PrerequisiteAdjustments(
			Subject.FurtherMaths, Rating.Green, [group],
			(_, requires) => requires == PrerequisiteSatisfaction.Chosen);

		adjustments.Should().BeEmpty();
	}

	[Theory]
	// qualifying mode: a green/amber rating OR a committed choice satisfies.
	[InlineData(PrerequisiteSatisfaction.Qualifying, true, false, true)]
	[InlineData(PrerequisiteSatisfaction.Qualifying, false, true, true)]
	[InlineData(PrerequisiteSatisfaction.Qualifying, false, false, false)]
	// chosen mode: only a committed choice satisfies — a merely-qualifying subject does not.
	[InlineData(PrerequisiteSatisfaction.Chosen, true, false, false)]
	[InlineData(PrerequisiteSatisfaction.Chosen, false, true, true)]
	public void prerequisite_availability_respects_the_satisfaction_mode(
		PrerequisiteSatisfaction requires, bool qualifies, bool chosen, bool expected)
	{
		IReadOnlyList<Subject> chosenALevels = chosen ? [Subject.Maths] : [];

		var available = ConstraintPass.IsPrerequisiteAvailable(
			Subject.Maths, requires, _ => qualifies, chosenALevels);

		available.Should().Be(expected);
	}

	[Fact]
	public void mutual_exclusion_downgrades_only_the_lower_weight_subject_to_amber()
	{
		var adjustments = ConstraintPass.Evaluate(
			Ratings((Subject.History, Rating.Green), (Subject.Art, Rating.Green)), Profile(), Harness.Catalogue);

		var (loser, winner) =
			Catalogue.Meta(Subject.History).UcasWeight < Catalogue.Meta(Subject.Art).UcasWeight
				? (Subject.History, Subject.Art)
				: (Subject.Art, Subject.History);

		var exclusion = adjustments.Should().ContainSingle().Which;
		exclusion.Subject.Should().Be(loser);
		exclusion.From.Should().Be(Rating.Green);
		exclusion.To.Should().Be(Rating.Amber);
		exclusion.Reason.Should().Contain("Mutual exclusion").And.Contain(EnumNames.NameOf(winner));
	}

	[Fact]
	public void red_severity_mutual_exclusion_downgrades_the_lower_weight_subject_to_red()
	{
		var adjustments = ConstraintPass.Evaluate(
			Ratings((Subject.French, Rating.Green), (Subject.German, Rating.Green)), Profile(), Harness.Catalogue);

		var loser = Catalogue.Meta(Subject.French).UcasWeight < Catalogue.Meta(Subject.German).UcasWeight
			? Subject.French
			: Subject.German;
		var winner = loser == Subject.French ? Subject.German : Subject.French;

		var exclusion = adjustments.Should().ContainSingle().Which;
		exclusion.Subject.Should().Be(loser);
		exclusion.From.Should().Be(Rating.Green);
		exclusion.To.Should().Be(Rating.Red);
		exclusion.Reason.Should().Contain("Mutual exclusion").And.Contain(EnumNames.NameOf(winner));
	}

	[Fact]
	public void chosen_a_level_can_force_a_red_exclusion()
	{
		var ratings = Ratings((Subject.French, Rating.Green), (Subject.German, Rating.Red));
		var profile = Profile() with { ChosenALevels = [Subject.German] };

		var adjustments = ConstraintPass.Evaluate(ratings, profile, Harness.Catalogue);

		var exclusion = adjustments.Should().ContainSingle().Which;
		exclusion.Subject.Should().Be(Subject.French);
		exclusion.From.Should().Be(Rating.Green);
		exclusion.To.Should().Be(Rating.Red);
		exclusion.Reason.Should().Contain("chosen german");
	}

	[Fact]
	public void chosen_a_level_can_force_an_amber_exclusion()
	{
		var ratings = Ratings((Subject.History, Rating.Green), (Subject.Art, Rating.Red));
		var profile = Profile() with { ChosenALevels = [Subject.Art] };

		var adjustments = ConstraintPass.Evaluate(ratings, profile, Harness.Catalogue);

		var exclusion = adjustments.Should().ContainSingle().Which;
		exclusion.Subject.Should().Be(Subject.History);
		exclusion.From.Should().Be(Rating.Green);
		exclusion.To.Should().Be(Rating.Amber);
		exclusion.Reason.Should().Contain("chosen art");
	}

	[Fact]
	public void mutual_exclusion_fires_only_when_both_subjects_are_green()
	{
		var adjustments = ConstraintPass.Evaluate(
			Ratings((Subject.History, Rating.Green), (Subject.Art, Rating.Amber)), Profile(), Harness.Catalogue);

		adjustments.Should().BeEmpty();
	}

	[Fact]
	public void chosen_a_levels_do_not_remove_the_chosen_subject_from_rating()
	{
		var ratings = Ratings(
			(Subject.French, Rating.Green), (Subject.Maths, Rating.Green),
			(Subject.Physics, Rating.Green), (Subject.Chemistry, Rating.Green),
			(Subject.Biology, Rating.Green), (Subject.History, Rating.Green));
		var profile = Profile() with { ChosenALevels = [Subject.French] };

		var final = ConstraintPass.Apply(ratings, ConstraintPass.Evaluate(ratings, profile, Harness.Catalogue));

		final.Should().ContainSingle(r => r.Subject == Subject.French && r.Rating == Rating.Green);
	}

	[Fact]
	public void own_time_requirement_downgrades_music_without_the_hobby()
	{
		var adjustments = ConstraintPass.Evaluate(Ratings((Subject.Music, Rating.Green)), Profile(), Harness.Catalogue);

		var ownTime = adjustments.Should().ContainSingle().Which;
		ownTime.Subject.Should().Be(Subject.Music);
		ownTime.From.Should().Be(Rating.Green);
		ownTime.To.Should().Be(Rating.Amber);
		ownTime.Reason.Should().Be(ConstraintPass.OwnTimeReason);
	}

	[Fact]
	public void own_time_requirement_is_satisfied_by_a_matching_hobby()
	{
		ConstraintPass.Evaluate(Ratings((Subject.Music, Rating.Green)), Profile("plays_piano"), Harness.Catalogue)
			.Should().BeEmpty();
	}

	[Fact]
	public void already_amber_music_without_the_hobby_records_the_own_time_authorisation_requirement()
	{
		var adjustments = ConstraintPass.Evaluate(Ratings((Subject.Music, Rating.Amber)), Profile(), Harness.Catalogue);

		var ownTime = adjustments.Should().ContainSingle().Which;
		ownTime.Subject.Should().Be(Subject.Music);
		ownTime.From.Should().Be(Rating.Amber);
		ownTime.To.Should().Be(Rating.Amber);
		ownTime.Reason.Should().Be(ConstraintPass.OwnTimeReason);
	}

	[Fact]
	public void student_under_all_constraints_has_no_adjustments()
	{
		// Maths is a committed choice (FM's chosen-mode prerequisite met), no both-green exclusion pair
		// (Art red), Music has its hobby.
		var ratings = Ratings(
			(Subject.Maths, Rating.Green), (Subject.FurtherMaths, Rating.Green),
			(Subject.History, Rating.Green), (Subject.Music, Rating.Green));
		var profile = Profile("plays_guitar") with { ChosenALevels = [Subject.Maths] };

		ConstraintPass.Evaluate(ratings, profile, Harness.Catalogue).Should().BeEmpty();
	}

	[Fact]
	public void adjustments_commute_so_applying_them_is_order_independent()
	{
		// Two independent downgrades in one pass: an exclusion (History/Art) and an own-time (Music).
		var ratings = Ratings(
			(Subject.Maths, Rating.Green), (Subject.History, Rating.Green),
			(Subject.Art, Rating.Green), (Subject.Music, Rating.Green));
		var adjustments = ConstraintPass.Evaluate(ratings, Profile(), Harness.Catalogue);

		var forward = ConstraintPass.Apply(ratings, adjustments);
		var reversed = ConstraintPass.Apply(ratings, [.. adjustments.Reverse()]);

		// Order-independent: the monotone downgrades compose to the same final ratings either way.
		forward.Should().BeEquivalentTo(reversed);

		var loser = Catalogue.Meta(Subject.History).UcasWeight < Catalogue.Meta(Subject.Art).UcasWeight
			? Subject.History
			: Subject.Art;
		Of(forward, loser).Should().Be(Rating.Amber);
		Of(forward, Subject.Music).Should().Be(Rating.Amber);
		Of(forward, Subject.Maths).Should().Be(Rating.Green); // untouched
	}

	[Fact]
	public void apply_composes_the_most_severe_rating()
	{
		// Further Maths base amber, Maths red ⇒ prerequisite forces red (most-severe wins).
		var ratings = Ratings((Subject.FurtherMaths, Rating.Amber));
		var adjustments = ConstraintPass.Evaluate(ratings, Profile(), Harness.Catalogue);

		var final = ConstraintPass.Apply(ratings, adjustments);

		Of(final, Subject.FurtherMaths).Should().Be(Rating.Red);
		final.Single(r => r.Subject == Subject.FurtherMaths).Reason.Should().Be(ConstraintPass.MathsPrerequisiteReason);
	}
}
