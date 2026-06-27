namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;
using Prediction;

/// <summary>
///     Phase 3 — per-subject entry requirements + green/amber/red rating tiers as ordered workflow rules,
///     driven <em>through the engine</em>. Expected ratings are hand-computed from §1.4 (entry thresholds)
///     and the <see cref="PredictionModel" /> / <see cref="ALevelGrade" /> tiers, not recomputed with the
///     rules' own logic — a transposed comparison in the JSON has to break a test.
/// </summary>
public sealed class Phase3Tests
{
	private static async Task<IReadOnlyList<SubjectRating>> RateAsync(params (string Subject, int Grade)[] gcses)
	{
		var student = new StudentInput("S-TEST", gcses.ToDictionary(g => g.Subject, g => g.Grade), []);
		var evaluator = await Harness.ShippedEvaluatorAsync();
		return await evaluator.EvaluateRatingsAsync(GradePredictor.Predict(student, Harness.AsOf), student.ToGcseResults());
	}

	private static Rating Of(IEnumerable<SubjectRating> ratings, Subject subject) =>
		ratings.Single(r => r.Subject == subject).Rating;

	// A full set of GCSEs at one uniform grade, so the average equals that grade.
	private static (string, int)[] Uniform(int grade) => [
		("maths", grade), ("english_language", grade), ("physics", grade),
		("chemistry", grade), ("biology", grade), ("english_literature", grade),
		("french", grade), ("german", grade), ("physical_education", grade),
		("computer_studies", grade), ("history", grade), ("music", grade), ("art", grade),
	];

	[Fact]
	public async Task top_student_is_green_in_every_subject()
	{
		// Average 9.0: every entry requirement is met and every predicted grade clears its green threshold.
		var ratings = await RateAsync(Uniform(9));

		ratings.Should().OnlyContain(r => r.Rating == Rating.Green);
	}

	[Fact]
	public async Task weak_student_is_red_in_every_subject()
	{
		// Average 4.0: every entry requirement fails (supporting GCSEs and the average are all too low).
		var ratings = await RateAsync(Uniform(4));

		ratings.Should().OnlyContain(r => r.Rating == Rating.Red);
	}

	[Fact]
	public async Task exactly_one_rating_per_subject()
	{
		var ratings = await RateAsync(Uniform(6));

		ratings.Select(r => r.Subject).Should().BeEquivalentTo(Catalogue.Subjects);
		ratings.Should().HaveCount(Catalogue.Subjects.Count);
	}

	[Fact]
	public async Task english_lit_entry_requires_both_english_and_maths_at_top_entry()
	{
		// English Language one below TopEntry: the entry conjunction fails ⇒ red, regardless of prediction.
		var below = await RateAsync(("english_language", Harness.Thresholds.TopEntry - 1), ("maths", Harness.Thresholds.TopEntry));
		Of(below, Subject.EnglishLiterature).Should().Be(Rating.Red);

		// Both at TopEntry (average 7.0): entry met and predicted 4.15 clears the green (B) threshold.
		var both = await RateAsync(("english_language", Harness.Thresholds.TopEntry), ("maths", Harness.Thresholds.TopEntry));
		Of(both, Subject.EnglishLiterature).Should().Be(Rating.Green);
	}

	[Fact]
	public async Task maths_tier_boundary_straddles_the_green_threshold()
	{
		// Maths GCSE 7, average 7.0 ⇒ predicted 4.6: clears amber (B) but not green (A) ⇒ amber.
		Of(await RateAsync(("maths", 7)), Subject.Maths).Should().Be(Rating.Amber);

		// Adding a 9 lifts the average to 8.0 ⇒ predicted 5.4: clears the green (A) point threshold, and at
		// the 8-to-9 prior-attainment band Maths P(≥A) ≈ 0.84 clears the green DfE confidence floor ⇒ green.
		Of(await RateAsync(("maths", 7), ("art", 9)), Subject.Maths).Should().Be(Rating.Green);
	}

	[Fact]
	public async Task green_dfe_confidence_floor_demotes_a_points_eligible_subject_to_amber()
	{
		// Music GCSE 9 with average 6.75 ⇒ predicted 4.04: clears the green point threshold (Music green tests
		// predicted ≥ B). But at the 6-to-7 prior-attainment band Music P(≥B) ≈ 0.52 sits between the amber
		// (0.50) and green (0.60) DfE confidence floors, so green is blocked on confidence and the subject is
		// amber. Reverting the green floor to the amber floor would flip this back to green and break here.
		var ratings = await RateAsync(("music", 9), ("history", 6), ("art", 6), ("biology", 6));

		Of(ratings, Subject.Music).Should().Be(Rating.Amber);
	}

	[Fact]
	public async Task maths_green_rule_requires_dfe_transition_probability_evidence()
	{
		var profile = new StudentProfile(
			"S-TEST",
			7.5,
			[new(Subject.Maths, ALevelGrade.A)],
			[],
			[]);
		var evaluator = await Harness.ShippedEvaluatorAsync();

		var ratings = await evaluator.EvaluateRatingsAsync(profile, [new("maths", Harness.Thresholds.TopEntry)]);

		// The point prediction clears green and amber, but both tiers now consume DfE probability evidence.
		// With no matrix row on the profile, both tiers are blocked and red wins.
		Of(ratings, Subject.Maths).Should().Be(Rating.Red);
	}

	[Fact]
	public async Task science_green_rules_require_dfe_transition_probability_evidence()
	{
		var profile = new StudentProfile(
			"S-TEST",
			8.0,
			[new(Subject.Physics, ALevelGrade.B)],
			[],
			[]);
		var evaluator = await Harness.ShippedEvaluatorAsync();

		var ratings = await evaluator.EvaluateRatingsAsync(
			profile,
			[new("maths", Harness.Thresholds.TopEntry), new("physics", Harness.Thresholds.StrongEntry)]);

		// Physics clears entry and the predicted B green tier, but both green and amber are blocked without the DfE row.
		Of(ratings, Subject.Physics).Should().Be(Rating.Red);
	}

	[Fact]
	public async Task amber_rules_require_dfe_transition_probability_evidence()
	{
		var profile = new StudentProfile(
			"S-TEST",
			6.0,
			[new(Subject.History, ALevelGrade.C)],
			[],
			[]);
		var evaluator = await Harness.ShippedEvaluatorAsync();

		var ratings = await evaluator.EvaluateRatingsAsync(profile, []);

		// History clears average entry and the predicted C amber tier, but amber is blocked without DfE evidence.
		Of(ratings, Subject.History).Should().Be(Rating.Red);
	}

	[Fact]
	public async Task english_lit_tier_boundary_straddles_the_amber_threshold()
	{
		// Entry met both times; the average moves the prediction across the amber/green tier line.
		// Average 6.0 ⇒ predicted 3.3: amber. Average 7.0 ⇒ predicted 4.15: green.
		var amber = await RateAsync(
			("english_language", 7), ("maths", 7), ("geography", 5), ("french", 5));
		Of(amber, Subject.EnglishLiterature).Should().Be(Rating.Amber);

		var green = await RateAsync(("english_language", 7), ("maths", 7));
		Of(green, Subject.EnglishLiterature).Should().Be(Rating.Green);
	}

	[Fact]
	public async Task missing_supporting_subject_fails_entry_to_red()
	{
		// Strong Maths but no Physics GCSE: Maths is green, Physics fails its supporting-GCSE entry ⇒ red.
		var ratings = await RateAsync(("maths", 9));

		Of(ratings, Subject.Maths).Should().Be(Rating.Green);
		Of(ratings, Subject.Physics).Should().Be(Rating.Red);
	}

	[Fact]
	public async Task every_rating_carries_a_reason()
	{
		var ratings = await RateAsync(Uniform(6));

		ratings.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Reason));
	}

	[Fact]
	public async Task shipped_subject_ratings_workflow_probe_compiles()
	{
		var (workflows, engine) = await Harness.BuildFromShippedWorkflowsAsync();

		var act = async () => await WorkflowStore.ProbeCompileAsync(engine, workflows, Harness.CanonicalProbe());

		await act.Should().NotThrowAsync();
	}
}
