namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;

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
	public void apply_leaves_subjects_without_adjustments_untouched()
	{
		SubjectRating[] ratings = [new(Subject.Maths, Rating.Green, "base reason")];

		var applied = ConstraintPass.Apply(ratings, []);

		applied.Should().ContainSingle().Which.Should().Be(new SubjectRating(Subject.Maths, Rating.Green, "base reason"));
	}
}
