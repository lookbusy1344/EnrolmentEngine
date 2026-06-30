namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;

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
	private static async Task<Rating> RateArtAsync(DateOnly dateOfBirth, int artGcse)
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

		var evaluator = await Harness.ShippedEvaluatorAsync();
		var ratings = await evaluator.EvaluateRatingsAsync(Harness.Predict(student), student.ToGcseResults());
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
	public async Task a_younger_student_clears_art_entry_at_the_strong_threshold() =>
		// Art GCSE exactly at StrongEntry: a sub-adult meets entry, so the rating is decided by prediction, not the gate.
		(await RateArtAsync(YoungerDob, Harness.Thresholds.StrongEntry)).Should().NotBe(Rating.Red);

	[Fact]
	public async Task an_adult_fails_art_entry_at_the_strong_threshold() =>
		// The same art GCSE that suffices for a younger student is below the adult's TopEntry bar ⇒ entry unmet ⇒ red.
		(await RateArtAsync(AdultDob, Harness.Thresholds.StrongEntry)).Should().Be(Rating.Red);

	[Fact]
	public async Task an_adult_clears_art_entry_at_the_top_threshold() =>
		// Raising the art GCSE to TopEntry meets the adult bar, so the gate no longer forces red.
		(await RateArtAsync(AdultDob, Harness.Thresholds.TopEntry)).Should().NotBe(Rating.Red);
}
