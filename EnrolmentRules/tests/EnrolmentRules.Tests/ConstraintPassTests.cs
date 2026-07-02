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
			new(Subject.French, Rating.Green, Rating.Amber, "first amber"),
			new(Subject.French, Rating.Green, Rating.Amber, "second amber"),
		];

		var applied = ConstraintPass.Apply(ratings, adjustments);

		var french = applied.Should().ContainSingle().Which;
		french.Rating.Should().Be(Rating.Amber);
		french.Reason.Should().Be("first amber");
	}

	[Fact]
	public void apply_takes_the_most_severe_adjustment_regardless_of_order()
	{
		SubjectRating[] ratings = [new(Subject.French, Rating.Green, "base reason")];
		Adjustment[] adjustments = [
			new(Subject.French, Rating.Green, Rating.Amber, "amber reason"),
			new(Subject.French, Rating.Green, Rating.Red, "red reason"),
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
		Adjustment[] adjustments = [new(Subject.Music, Rating.Red, Rating.Red, "veto reason")];

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
		Adjustment[] adjustments = [new(Subject.Biology, Rating.Red, Rating.Amber, "less severe reason")];

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
