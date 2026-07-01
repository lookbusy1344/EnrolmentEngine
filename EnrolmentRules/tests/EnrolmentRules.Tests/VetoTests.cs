namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;

/// <summary>
///     Phase 9 — the per-subject veto (§1.5). A single-student, single-subject hard bar: when an
///     incompatible activity is present the subject is forced red, overriding its entry/green/amber tier.
///     It is own-time's mirror in the constraint pass — same <see cref="StudentProfile.Hobbies" /> input,
///     but <em>presence</em> triggers a red veto that wins over green/amber, where own-time's
///     <em>absence</em> only demotes a green to amber. Like every adjustment it only downgrades, so it
///     composes by most-severe-wins. Unlike the other rules it fires even on an already-red base: the bar
///     is the most informative reason, so it replaces "entry unmet" with the specific incompatibility.
/// </summary>
public sealed class VetoTests
{
	// Music carries the illustrative veto: a `plays_trombone` hobby bars Music outright — even though that
	// same hobby satisfies Music's own-time `plays_` requirement, demonstrating the veto wins over the tier.
	private const string TromboneHobby = "plays_trombone";

	private static SubjectRating[] Ratings(params (Subject Subject, Rating Rating)[] overrides)
	{
		var map = Catalogue.Subjects.ToDictionary(static s => s, static _ => Rating.Red);
		foreach (var (subject, rating) in overrides) {
			map[subject] = rating;
		}

		return [.. map.Select(static kv => new SubjectRating(kv.Key, kv.Value, "base"))];
	}

	private static StudentProfile Profile(params string[] hobbies) => new("S", 7.0, [], [], hobbies);

	private static CatalogueData MusicPrerequisiteCatalogue()
	{
		var shipped = Harness.Catalogue;
		var entries = shipped.Subjects.ToDictionary(
			static subject => subject,
			subject => subject == Subject.Music
				? shipped.Meta(subject) with { Prerequisites = [new([Subject.Maths], Rating.Red)] }
				: shipped.Meta(subject));

		return new(entries, shipped.Subjects);
	}

	private static StudentInput StrongStudent(params string[] hobbies) =>
		new("S-VETO", new Dictionary<string, int> {
			["english_language"] = 8,
			["maths"] = 8,
			["physics"] = 8,
			["chemistry"] = 8,
			["biology"] = 8,
			["english_literature"] = 8,
			["french"] = 8,
			["german"] = 8,
			["physical_education"] = 8,
			["computer_studies"] = 8,
			["history"] = 8,
			["music"] = 8,
			["art"] = 8,
		}, hobbies);

	[Fact]
	public void veto_forces_red_when_an_incompatible_activity_is_present()
	{
		var adjustments = ConstraintPass.Evaluate(Ratings((Subject.Music, Rating.Green)), Profile(TromboneHobby), Harness.Catalogue);

		var veto = adjustments.Should().ContainSingle().Which;
		veto.Subject.Should().Be(Subject.Music);
		veto.From.Should().Be(Rating.Green);
		veto.To.Should().Be(Rating.Red);
		veto.Reason.Should().StartWith(ConstraintPass.VetoReasonPrefix).And.Contain(TromboneHobby);
	}

	[Fact]
	public void veto_overrides_an_amber_base()
	{
		ConstraintPass.Evaluate(Ratings((Subject.Music, Rating.Amber)), Profile(TromboneHobby), Harness.Catalogue)
			.Should().ContainSingle().Which.To.Should().Be(Rating.Red);
	}

	[Fact]
	public void veto_relabels_an_already_red_subject_with_the_specific_bar()
	{
		// The rating is red either way, but the veto is the more informative reason, so it still fires.
		var veto = ConstraintPass.Evaluate(Ratings((Subject.Music, Rating.Red)), Profile(TromboneHobby), Harness.Catalogue)
			.Should().ContainSingle().Which;
		veto.To.Should().Be(Rating.Red);
		veto.Reason.Should().Contain(TromboneHobby);
	}

	[Fact]
	public void a_veto_reason_wins_over_an_unmet_prerequisite_reason_for_the_same_subject()
	{
		var catalogue = MusicPrerequisiteCatalogue();
		var ratings = Ratings((Subject.Music, Rating.Green));
		var profile = Profile(TromboneHobby);

		var adjustments = ConstraintPass.Evaluate(ratings, profile, catalogue);
		adjustments.Should().HaveCount(2);

		var applied = ConstraintPass.Apply(ratings, adjustments);

		applied.Single(r => r.Subject == Subject.Music).Reason.Should().StartWith(ConstraintPass.VetoReasonPrefix);
	}

	[Fact]
	public void no_veto_when_the_incompatible_activity_is_absent()
	{
		// A piano player satisfies Music's own-time requirement and triggers no veto.
		ConstraintPass.Evaluate(Ratings((Subject.Music, Rating.Green)), Profile("plays_piano"), Harness.Catalogue)
			.Should().BeEmpty();
	}

	[Fact]
	public async Task veto_overrides_a_green_music_end_to_end()
	{
		var engine = await Harness.ShippedEngineAsync();

		var result = engine.Evaluate(StrongStudent(TromboneHobby));

		// Without the trombone Music is green for this student; the veto must force it red through the
		// whole pipeline (engine tier → constraint pass → cap), citing the incompatible activity.
		var music = result.Recommendations.Single(r => r.Subject == Subject.Music);
		music.Rating.Should().Be(Rating.Red);
		music.Reason.Should().Contain(TromboneHobby);
	}
}
