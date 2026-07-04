namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     The age-gated entry demonstration (§1.1 per-student attributes): <c>date_of_birth</c> is the raw
///     student fact; the prediction stage derives a whole-years age from it as of the run's reference date
///     (<see cref="Harness.AsOf" />) and threads it to <see cref="RatingFacts.Age" />, which a workflow lambda
///     consumes — the gate <em>policy</em> is data in <c>subject-ratings.yaml</c>, not host code. Art's entry
///     threshold is age-conditional: an adult (≥ <see cref="PolicyThresholds.AdultAge" />) must reach
///     <see cref="PolicyThresholds.TopEntry" /> at GCSE, a younger student only <see cref="PolicyThresholds.StrongEntry" />.
///     These run <em>through the engine</em>, so a transposed comparison or a dropped <c>facts.Age</c> binding
///     in the YAML breaks a test.
/// </summary>
public sealed class AgeGateTests
{
	// Births that land exactly on Younger/Adult ages on the fixed reference date (birthday on the as-of day).
	private static readonly DateOnly YoungerDob = Harness.AsOf.AddYears(-(Harness.Thresholds.AdultAge - 1));
	private static readonly DateOnly AdultDob = Harness.AsOf.AddYears(-Harness.Thresholds.AdultAge);

	// A strong, eligible student whose art prediction comfortably clears its tiers, so the only swing factor
	// left for art is the age-conditional GCSE entry threshold. Art GCSE and date of birth are caller-supplied.
	private static Rating RateArt(DateOnly dateOfBirth, int artGcse)
	{
		var student = new StudentInput(
			"S-AGE",
			new Dictionary<string, int> {
				["maths"] = 9,
				["english_language"] = 9,
				["chemistry"] = 9,
				["biology"] = 9,
				["history"] = 9,
				["art"] = artGcse,
			},
			[]) { DateOfBirth = dateOfBirth };

		var evaluator = Harness.ShippedEvaluator();
		var ratings = evaluator.EvaluateRatings(Harness.Predict(student), student.ToGcseResults());
		return ratings.Single(r => r.Subject == Subject.Art).Rating;
	}

	[Fact]
	public void age_is_derived_from_date_of_birth_as_of_the_reference_date()
	{
		AgeCalculator.WholeYears(YoungerDob, Harness.AsOf).Should().Be(Harness.Thresholds.AdultAge - 1);
		AgeCalculator.WholeYears(AdultDob, Harness.AsOf).Should().Be(Harness.Thresholds.AdultAge);

		// The day before the adult's birthday, they are still sub-adult — the gate tracks the reference date.
		AgeCalculator.WholeYears(AdultDob, Harness.AsOf.AddDays(-1)).Should().Be(Harness.Thresholds.AdultAge - 1);
	}

	[Fact]
	public void a_29_february_birth_ages_up_on_1_march_in_non_leap_years()
	{
		// UK legal convention: someone born on 29 Feb is not deemed a year older until 1 March in a
		// non-leap year (the anniversary date does not exist), not 28 Feb.
		var dob = new DateOnly(2024, 2, 29);

		AgeCalculator.WholeYears(dob, new(2025, 2, 28)).Should().Be(0);
		AgeCalculator.WholeYears(dob, new(2025, 3, 1)).Should().Be(1);
		AgeCalculator.WholeYears(dob, new(2026, 2, 28)).Should().Be(1);
		AgeCalculator.WholeYears(dob, new(2026, 3, 1)).Should().Be(2);
		AgeCalculator.WholeYears(dob, new(2027, 2, 28)).Should().Be(2);
		AgeCalculator.WholeYears(dob, new(2027, 3, 1)).Should().Be(3);

		// 2028 is a leap year, so the anniversary genuinely falls on 29 Feb again.
		AgeCalculator.WholeYears(dob, new(2028, 2, 28)).Should().Be(3);
		AgeCalculator.WholeYears(dob, new(2028, 2, 29)).Should().Be(4);
	}

	[Fact]
	public void a_younger_student_clears_art_entry_at_the_strong_threshold() =>
		// Art GCSE exactly at StrongEntry: a sub-adult meets entry, so the rating is decided by prediction, not the gate.
		RateArt(YoungerDob, Harness.Thresholds.StrongEntry).Should().NotBe(Rating.Red);

	[Fact]
	public void an_adult_fails_art_entry_at_the_strong_threshold() =>
		// The same art GCSE that suffices for a younger student is below the adult's TopEntry bar ⇒ entry unmet ⇒ red.
		RateArt(AdultDob, Harness.Thresholds.StrongEntry).Should().Be(Rating.Red);

	[Fact]
	public void an_adult_clears_art_entry_at_the_top_threshold() =>
		// Raising the art GCSE to TopEntry meets the adult bar, so the gate no longer forces red.
		RateArt(AdultDob, Harness.Thresholds.TopEntry).Should().NotBe(Rating.Red);
}
