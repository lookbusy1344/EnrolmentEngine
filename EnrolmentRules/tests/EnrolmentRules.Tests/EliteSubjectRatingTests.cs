namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 3.1 — the subject boundary matrix for all fourteen offered
///     subjects, driven through the real Elite engine (<see cref="EliteHarness" />).
/// </summary>
public sealed class EliteSubjectRatingTests
{
	private static StudentInput EligibleStudent(Dictionary<string, int> gcses) =>
		new("S-ELITE", gcses, []) {
			DateOfBirth = new(2009, 9, 1),
		};

	public static TheoryData<string> CognateSubjects() => [
		"biology", "chemistry", "history", "maths", "physics", "psychology",
		"english_language", "english_literature", "french", "geography", "politics",
	];

	[Theory]
	[MemberData(nameof(CognateSubjects))]
	public void matching_grade_seven_is_red_and_grade_eight_is_selectable(string cognateGcse)
	{
		var subject = new Subject(cognateGcse);
		var engine = EliteHarness.ShippedEngine();
		var gcses = EliteHarness.AllOfferedGcsesAtGrade(8);

		gcses[cognateGcse] = 7;
		var atSeven = engine.Explain(EligibleStudent(gcses));

		gcses[cognateGcse] = 8;
		var atEight = engine.Explain(EligibleStudent(gcses));

		atSeven.Explanations.Single(e => e.Subject == subject).Rating.Should().Be(Rating.Red);
		atEight.Explanations.Single(e => e.Subject == subject).Rating.Should().NotBe(Rating.Red);
	}

	[Fact]
	public void economics_related_discipline_is_gcse_maths_at_grade_eight()
	{
		var engine = EliteHarness.ShippedEngine();
		var gcses = EliteHarness.AllOfferedGcsesAtGrade(8);

		gcses["maths"] = 7;
		var atSeven = engine.Explain(EligibleStudent(gcses));

		// Raising maths to 7 also breaks the MathsPass eligibility gate (Elite requires grade 7 minimum,
		// so 7 alone still clears eligibility) — Economics reads the same GCSE at the higher standard_entry
		// bar (8), independently of the eligibility gate's lower pass bar.
		atSeven.Eligible.Should().BeTrue();
		atSeven.Explanations.Single(e => e.Subject == Subject.Economics).Rating.Should().Be(Rating.Red);

		gcses["maths"] = 8;
		var atEight = engine.Explain(EligibleStudent(gcses));

		atEight.Explanations.Single(e => e.Subject == Subject.Economics).Rating.Should().NotBe(Rating.Red);
	}

	[Fact]
	public void religious_studies_related_discipline_is_gcse_history_at_grade_eight()
	{
		var engine = EliteHarness.ShippedEngine();
		var gcses = EliteHarness.AllOfferedGcsesAtGrade(8);

		gcses["history"] = 7;
		var atSeven = engine.Explain(EligibleStudent(gcses));
		atSeven.Explanations.Single(e => e.Subject == Subject.ReligiousStudies).Rating.Should().Be(Rating.Red);
		// History's own tier reads the same GCSE at the same bar, so it is red too at grade 7.
		atSeven.Explanations.Single(e => e.Subject == Subject.History).Rating.Should().Be(Rating.Red);

		gcses["history"] = 8;
		var atEight = engine.Explain(EligibleStudent(gcses));
		atEight.Explanations.Single(e => e.Subject == Subject.ReligiousStudies).Rating.Should().NotBe(Rating.Red);
	}

	[Fact]
	public void further_maths_straddles_gcse_maths_eight_and_nine()
	{
		var engine = EliteHarness.ShippedEngine();
		var gcses = EliteHarness.AllOfferedGcsesAtGrade(9);

		gcses["maths"] = 8;
		var atEight = engine.Explain(EligibleStudent(gcses) with {
			ChosenALevels = [Subject.Maths],
		});

		gcses["maths"] = 9;
		var atNine = engine.Explain(EligibleStudent(gcses) with {
			ChosenALevels = [Subject.Maths],
		});

		atEight.Explanations.Single(e => e.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Red);
		atNine.Explanations.Single(e => e.Subject == Subject.FurtherMaths).Rating.Should().NotBe(Rating.Red);
	}

	[Fact]
	public void further_maths_requires_chosen_a_level_maths_even_at_gcse_grade_nine()
	{
		var engine = EliteHarness.ShippedEngine();
		var gcses = EliteHarness.AllOfferedGcsesAtGrade(9);

		var withoutChosenMaths = engine.Explain(EligibleStudent(gcses));

		withoutChosenMaths.Explanations.Single(e => e.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Red);
	}

	[Fact]
	public void a_student_meeting_the_hard_entry_rule_is_never_red_merely_because_prediction_or_dfe_evidence_is_weak()
	{
		var engine = EliteHarness.ShippedEngine();
		// Grade exactly at the entry bar (8): amber (the hard entry clause alone) at worst, never red,
		// since red is an unconditional catch-all that only fires when entry itself is unmet.
		var gcses = EliteHarness.AllOfferedGcsesAtGrade(8);

		var result = engine.Explain(EligibleStudent(gcses));

		// Further Maths is excluded: its chosen-Mathematics prerequisite is a separate host-code
		// constraint (§ catalogue.yaml), not part of this "entry-met never red" rating-tier claim, and
		// this fixture makes no A-level choice.
		foreach (var subject in EliteHarness.ShippedEngine().Catalogue.Subjects.Where(static s => s != Subject.FurtherMaths)) {
			result.Explanations.Single(e => e.Subject == subject).Rating.Should().NotBe(Rating.Red);
		}
	}

	[Fact]
	public void every_subject_has_exactly_one_winning_rating()
	{
		var engine = EliteHarness.ShippedEngine();
		var result = engine.Explain(EligibleStudent(EliteHarness.AllOfferedGcsesAtGrade(8)));

		foreach (var subject in EliteHarness.ShippedEngine().Catalogue.Subjects) {
			result.Explanations.Should().ContainSingle(e => e.Subject == subject);
		}
	}

	[Fact]
	public void only_the_fourteen_elite_subjects_appear_in_recommendations()
	{
		var engine = EliteHarness.ShippedEngine();
		var result = engine.Evaluate(EligibleStudent(EliteHarness.AllOfferedGcsesAtGrade(8)));

		result.Recommendations.Select(r => r.Subject).Should().BeEquivalentTo([
			Subject.Biology, Subject.Chemistry, Subject.Economics, Subject.EnglishLanguage, Subject.EnglishLiterature,
			Subject.French, Subject.FurtherMaths, Subject.Geography, Subject.History, Subject.Maths,
			Subject.Physics, Subject.Politics, Subject.Psychology, Subject.ReligiousStudies,
		]);
	}

	[Fact]
	public void no_standard_catalogue_relationship_leaks_into_elite()
	{
		// Standard's shipped catalogue excludes History <-> Art and requires own-time activities for
		// Music. Elite offers neither Art nor Music at all, and its History carries no exclusion unless
		// explicitly authored (it is not).
		var engine = EliteHarness.ShippedEngine();
		engine.Catalogue.Subjects.Should().NotContain([Subject.Art, Subject.Music]);

		var result = engine.Evaluate(EligibleStudent(EliteHarness.AllOfferedGcsesAtGrade(8)) with {
			ChosenALevels = [Subject.History],
		});

		result.Recommendations.Single(r => r.Subject == Subject.History).Rating.Should().NotBe(Rating.Red);
	}
}
