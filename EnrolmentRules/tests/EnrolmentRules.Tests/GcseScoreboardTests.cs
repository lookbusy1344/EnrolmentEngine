namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     The GCSE scoreboard is the live basket tally: count, summed points and mean over a student's graded
///     GCSEs on the 1–9 scale. Its average matches
///     <see cref="Prediction.GradePredictor.AverageGcseScore" />; the same cases are mirrored in
///     gcseScoreboard.test.ts.
/// </summary>
public sealed class GcseScoreboardTests
{
	[Fact]
	public void empty_scoreboard_has_zero_count_total_and_average()
	{
		var board = GcseScoreboard.From([]);

		board.Count.Should().Be(0);
		board.Total.Should().Be(0);
		board.Average.Should().Be(0.0);
	}

	[Fact]
	public void sums_and_averages_the_grades()
	{
		var board = GcseScoreboard.From([
			new("maths", 8),
			new("physics", 7),
			new("chemistry", 6),
		]);

		board.Count.Should().Be(3);
		board.Total.Should().Be(21);
		board.Average.Should().Be(7.0);
	}

	[Fact]
	public void average_can_be_fractional()
	{
		var board = GcseScoreboard.From([
			new("maths", 8),
			new("physics", 7),
		]);

		board.Average.Should().Be(7.5);
	}

	[Fact]
	public void average_matches_the_prediction_stage()
	{
		IReadOnlyList<GcseResult> gcses = [new("maths", 6), new("physics", 4), new("chemistry", 5)];

		GcseScoreboard.From(gcses).Average.Should().Be(Prediction.GradePredictor.AverageGcseScore(gcses));
	}
}
