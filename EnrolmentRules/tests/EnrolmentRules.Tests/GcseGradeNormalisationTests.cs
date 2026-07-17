namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Guards <see cref="Thresholds.NormalizeGcseGrade" />, the shared rule both web front-ends apply to a
///     raw typed grade. The TypeScript mirror in <c>ClientApp/src/state/gcseGrade.ts</c> is held to the same
///     cases by <c>gcseGrade.test.ts</c> — change one, change both.
/// </summary>
public sealed class GcseGradeNormalisationTests
{
	[Theory]
	[InlineData(1, 1)]
	[InlineData(9, 9)]
	[InlineData(5, 5)]
	public void Grades_already_on_the_scale_are_unchanged(double raw, int expected) =>
		Thresholds.NormalizeGcseGrade(raw).Should().Be(expected);

	[Theory]
	[InlineData(10, 9)]
	[InlineData(47, 9)]
	[InlineData(0, 1)]
	[InlineData(-3, 1)]
	public void Grades_off_the_scale_clamp_to_the_nearest_bound(double raw, int expected) =>
		Thresholds.NormalizeGcseGrade(raw).Should().Be(expected);

	[Theory]
	[InlineData(7.4, 7)]
	[InlineData(7.6, 8)]
	[InlineData(8.5, 9)]
	[InlineData(6.5, 7)] // half away from zero, matching JavaScript's Math.round in the Vue mirror
	[InlineData(1.2, 1)]
	public void Decimal_grades_round_to_the_nearest_integer(double raw, int expected) =>
		Thresholds.NormalizeGcseGrade(raw).Should().Be(expected);

	[Theory]
	[InlineData(9.6)]
	[InlineData(0.4)]
	public void Rounding_happens_before_clamping_so_a_rounded_value_never_leaves_the_scale(double raw) =>
		Thresholds.NormalizeGcseGrade(raw).Should().BeInRange(Thresholds.MinGcseGrade, Thresholds.MaxGcseGrade);
}
