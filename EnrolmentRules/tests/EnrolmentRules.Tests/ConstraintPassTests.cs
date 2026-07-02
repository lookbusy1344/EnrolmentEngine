namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;

/// <summary>
///     Direct unit tests for <see cref="ConstraintPass.Apply" />, pinning the two behaviours the
///     most-severe-wins fold depends on: the deterministic same-severity tie-break within a reason
///     precedence class (first emitted adjustment wins the reason) and the equal-severity no-op that
///     replaces only the reason (own-time amber→amber, veto red→red). These guard the apply fold
///     independently of which constraint rule happens to produce the adjustments.
/// </summary>
public sealed class ConstraintPassTests
{
	[Fact]
	public void evaluate_throws_a_clear_error_when_a_required_subject_rating_is_missing()
	{
		var ratings = new[] { new SubjectRating(Subject.French, Rating.Green, "base reason") };
		var profile = new StudentProfile("S", 7.0, [], [], ["plays_trombone"]);

		var act = () => ConstraintPass.Evaluate(ratings, profile, Harness.Catalogue);

		act.Should()
			.Throw<InvalidDataException>()
			.WithMessage("*base rating for subject 'music'*");
	}

	[Fact]
	public void apply_breaks_same_severity_ties_in_favour_of_the_first_adjustment()
	{
		SubjectRating[] ratings = [new(Subject.French, Rating.Green, "base reason")];
		Adjustment[] adjustments = [
			new(Subject.French, Rating.Green, Rating.Amber, AdjustmentKind.OwnTime, "first amber"),
			new(Subject.French, Rating.Green, Rating.Amber, AdjustmentKind.OwnTime, "second amber"),
		];

		var applied = ConstraintPass.Apply(ratings, adjustments);

		var french = applied.Should().ContainSingle().Which;
		french.Rating.Should().Be(Rating.Amber);
		french.Reason.Should().Be("first amber");
	}

	[Fact]
	public void apply_breaks_same_severity_ties_by_kind_precedence_not_reason_text()
	{
		// Two equally-severe (green→red) downgrades: the higher-precedence Kind supplies the surviving reason
		// regardless of array order. This pins the tie-break to the typed discriminator, not the reason text —
		// the coupling the old string-prefix precedence carried.
		SubjectRating[] ratings = [new(Subject.Music, Rating.Green, "base reason")];
		Adjustment[] prereqFirst = [
			new(Subject.Music, Rating.Green, Rating.Red, AdjustmentKind.Prerequisite, "prereq reason"),
			new(Subject.Music, Rating.Green, Rating.Red, AdjustmentKind.Veto, "veto reason"),
		];
		Adjustment[] vetoFirst = [.. prereqFirst.Reverse()];

		ConstraintPass.Apply(ratings, prereqFirst).Single().Reason.Should().Be("veto reason");
		ConstraintPass.Apply(ratings, vetoFirst).Single().Reason.Should().Be("veto reason");
	}

	[Fact]
	public void evaluate_stamps_each_downgrade_with_its_relationship_kind()
	{
		// A veto producer must stamp AdjustmentKind.Veto so the fold can order it without parsing the reason.
		SubjectRating[] ratings = [.. Harness.Catalogue.Subjects.Select(subject => new SubjectRating(subject, Rating.Green, "base"))];
		var profile = new StudentProfile("S", 7.0, [], [], ["plays_trombone"]);

		var adjustments = ConstraintPass.Evaluate(ratings, profile, Harness.Catalogue);

		adjustments.Should().Contain(adjustment => adjustment.Subject == Subject.Music && adjustment.Kind == AdjustmentKind.Veto);
	}

	[Fact]
	public void apply_takes_the_most_severe_adjustment_regardless_of_order()
	{
		SubjectRating[] ratings = [new(Subject.French, Rating.Green, "base reason")];
		Adjustment[] adjustments = [
			new(Subject.French, Rating.Green, Rating.Amber, AdjustmentKind.OwnTime, "amber reason"),
			new(Subject.French, Rating.Green, Rating.Red, AdjustmentKind.Veto, "red reason"),
		];

		var applied = ConstraintPass.Apply(ratings, adjustments);

		var french = applied.Should().ContainSingle().Which;
		french.Rating.Should().Be(Rating.Red);
		french.Reason.Should().Be("red reason");
	}

	[Fact]
	public void apply_replaces_only_the_reason_for_an_equal_severity_no_op_adjustment()
	{
		// The veto-on-already-red case: rating stays red, but the named bar is the more informative reason.
		SubjectRating[] ratings = [new(Subject.Music, Rating.Red, "base red reason")];
		Adjustment[] adjustments = [new(Subject.Music, Rating.Red, Rating.Red, AdjustmentKind.Veto, "veto reason")];

		var applied = ConstraintPass.Apply(ratings, adjustments);

		var music = applied.Should().ContainSingle().Which;
		music.Rating.Should().Be(Rating.Red);
		music.Reason.Should().Be("veto reason");
	}

	[Fact]
	public void an_amber_restudy_bar_does_not_override_a_red_base_rating_or_reason()
	{
		var profile = new StudentProfile(
			"S-RB-AMBER",
			7.5,
			[],
			[],
			[]) { PriorQualifications = [new(Subject.Biology.Value, QualificationType.ALevel, "e")] };
		var catalogue =
			new CatalogueData(
				new Dictionary<Subject, SubjectMeta> {
					[Subject.Biology] = Harness.Catalogue.Meta(Subject.Biology) with { RestudyBar = new([QualificationType.ALevel], Rating.Amber) },
				}, [Subject.Biology]);
		SubjectRating[] ratings = [new(Subject.Biology, Rating.Red, "failed entry requirement")];

		var adjustments = ConstraintPass.Evaluate(ratings, profile, catalogue);
		var applied = ConstraintPass.Apply(ratings, adjustments);

		adjustments.Should().BeEmpty();
		applied.Should().ContainSingle().Which.Should()
			.Be(new SubjectRating(Subject.Biology, Rating.Red, "failed entry requirement"));
	}

	[Fact]
	public void apply_ignores_an_adjustment_that_would_upgrade_the_base_rating()
	{
		SubjectRating[] ratings = [new(Subject.Biology, Rating.Red, "failed entry requirement")];
		Adjustment[] adjustments = [new(Subject.Biology, Rating.Red, Rating.Amber, AdjustmentKind.RestudyBar, "less severe reason")];

		var applied = ConstraintPass.Apply(ratings, adjustments);

		applied.Should().Equal(ratings);
	}

	[Fact]
	public void apply_leaves_subjects_without_adjustments_untouched()
	{
		SubjectRating[] ratings = [new(Subject.Maths, Rating.Green, "base reason")];

		var applied = ConstraintPass.Apply(ratings, []);

		applied.Should().ContainSingle().Which.Should().Be(new SubjectRating(Subject.Maths, Rating.Green, "base reason"));
	}

	[Fact]
	public void a_qualifying_prerequisite_is_unmet_when_its_dependency_is_downgraded_to_red_by_another_constraint()
	{
		// Economics requires Maths under the qualifying mode. Maths qualifies at its base rating, but a
		// blocking hobby vetoes it to red. The prerequisite must observe the vetoed rating and downgrade
		// Economics — not read Maths's stale green base and leave Economics green, recommending a subject
		// on a prerequisite the student can no longer take.
		var maths = Harness.Catalogue.Meta(Subject.Maths) with { BlockingActivities = ["hates_maths"] };
		var economics = Harness.Catalogue.Meta(Subject.Economics) with { Exclusions = [] };
		var catalogue = new CatalogueData(
			new Dictionary<Subject, SubjectMeta> {
				[Subject.Maths] = maths,
				[Subject.Economics] = economics,
			}, [Subject.Maths, Subject.Economics]);
		var profile = new StudentProfile("S-PREREQ-VETO", 7.0, [], [], ["hates_maths"]);
		SubjectRating[] ratings = [
			new(Subject.Maths, Rating.Green, "maths base"),
			new(Subject.Economics, Rating.Green, "economics base"),
		];

		var applied = ConstraintPass.Apply(ratings, ConstraintPass.Evaluate(ratings, profile, catalogue));

		applied.Single(rating => rating.Subject == Subject.Maths).Rating.Should().Be(Rating.Red);
		var economicsFinal = applied.Single(rating => rating.Subject == Subject.Economics);
		economicsFinal.Rating.Should().Be(Rating.Amber);
		economicsFinal.Reason.Should().Be(ConstraintPass.MathsPrerequisiteReason);
	}

	[Fact]
	public void a_qualifying_prerequisite_stays_met_when_its_dependency_keeps_a_qualifying_rating()
	{
		// The mirror of the veto case: with no constraint knocking Maths out, its green base still satisfies
		// Economics's qualifying prerequisite, so Economics is untouched. Guards against the phased evaluation
		// over-firing prerequisites.
		var economics = Harness.Catalogue.Meta(Subject.Economics) with { Exclusions = [] };
		var catalogue = new CatalogueData(
			new Dictionary<Subject, SubjectMeta> {
				[Subject.Maths] = Harness.Catalogue.Meta(Subject.Maths),
				[Subject.Economics] = economics,
			}, [Subject.Maths, Subject.Economics]);
		var profile = new StudentProfile("S-PREREQ-MET", 7.0, [], [], []);
		SubjectRating[] ratings = [
			new(Subject.Maths, Rating.Green, "maths base"),
			new(Subject.Economics, Rating.Green, "economics base"),
		];

		var adjustments = ConstraintPass.Evaluate(ratings, profile, catalogue);

		adjustments.Should().BeEmpty();
	}
}

/// <summary>
///     Engine-driven constraint-pass scenarios through the full pipeline. Explicit regression anchors
///     for the cross-subject downgrades, kept alongside the unit tests for Apply.
/// </summary>
public sealed class ConstraintPassScenarioTests
{
	private readonly EnrolmentEngine engine = Harness.ShippedEngine();

	private static StudentInput StrongEligibleStudent() =>
		new("S-MONOTONE", new Dictionary<string, int> {
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
		}, []) { DateOfBirth = new(2009, 9, 1) };

	[Fact]
	public void further_maths_without_chosen_maths_is_red_from_prerequisite_constraint()
	{
		var explained = engine.Explain(StrongEligibleStudent());

		explained.Eligible.Should().BeTrue();
		var furtherMaths = explained.Explanations.Single(explanation => explanation.Subject == Subject.FurtherMaths);
		furtherMaths.BaseRating.Should().NotBe(Rating.Red);
		furtherMaths.Rating.Should().Be(Rating.Red);
		furtherMaths.Overrides.Should().ContainSingle(override_ =>
			override_.To == Rating.Red && override_.Reason == ConstraintPass.MathsPrerequisiteReason);
	}

	[Fact]
	public void qualifying_french_and_german_mutual_exclusion_demotes_german_to_red()
	{
		var explained = engine.Explain(StrongEligibleStudent());

		explained.Eligible.Should().BeTrue();
		var french = explained.Explanations.Single(explanation => explanation.Subject == Subject.French);
		var german = explained.Explanations.Single(explanation => explanation.Subject == Subject.German);
		french.Rating.Should().Be(Rating.Green);
		german.BaseRating.Should().Be(Rating.Green);
		german.Rating.Should().Be(Rating.Red);
		german.Overrides.Should().ContainSingle(override_ =>
			override_.To == Rating.Red && override_.Reason.Contains("Mutual exclusion", StringComparison.Ordinal));
	}
}
