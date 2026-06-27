namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;
using Prediction;

/// <summary>
///     The restudy bar is the downgrade half of the new prior-qualification feature. It must demote a
///     subject when the student already holds a barred qualification in the same subject, but leave the
///     subject unchanged for unrelated prior qualifications.
/// </summary>
public sealed class RestudyBarTests
{
	private static StudentInput StrongStudent(params Qualification[] priorQualifications) =>
		new(
			"S-RB",
			new Dictionary<string, int> {
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
			},
			[]) { DateOfBirth = new DateOnly(2009, 9, 1), PriorQualifications = priorQualifications };

	[Fact]
	public void a_same_subject_prior_a_level_triggers_the_restudy_bar()
	{
		var profile = GradePredictor.Predict(
			StrongStudent(new Qualification(Subject.Biology.Value, QualificationType.ALevel, "e")),
			Harness.AsOf);

		var adjustments = ConstraintPass.Evaluate([
			new(Subject.Biology, Rating.Green, "base"),
		], profile, Harness.Catalogue);

		adjustments.Should().ContainSingle().Which.Should().Match<Adjustment>(adjustment =>
			adjustment.Subject == Subject.Biology
			&& adjustment.To == Rating.Red
			&& adjustment.Reason.Contains("already holds", StringComparison.Ordinal));
	}

	[Fact]
	public void a_prior_qualification_in_a_different_subject_does_not_trigger_the_bar()
	{
		var profile = GradePredictor.Predict(
			StrongStudent(new Qualification(Subject.Physics.Value, QualificationType.ALevel, "e")),
			Harness.AsOf);

		ConstraintPass.Evaluate([
				new(Subject.Biology, Rating.Green, "base"),
			], profile, Harness.Catalogue)
			.Should()
			.BeEmpty();
	}

	[Fact]
	public async Task the_restudy_bar_overrides_biology_through_the_engine()
	{
		var engine = await Harness.ShippedEngineAsync();
		var result = await engine.EvaluateAsync(StrongStudent(
			new Qualification(Subject.Biology.Value, QualificationType.ALevel, "e")));

		var biology = result.Recommendations.Single(r => r.Subject == Subject.Biology);
		biology.Rating.Should().Be(Rating.Red);
		biology.Reason.Should().Contain("already holds");
	}
}
