namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Phase 3 — per-subject entry requirements + green/amber/red rating tiers as ordered workflow rules,
///     driven <em>through the engine</em>. Expected ratings are hand-computed from §1.4 (entry thresholds)
///     and the <see cref="PredictionModel" /> / <see cref="ALevelGrade" /> tiers, not recomputed with the
///     rules' own logic — a transposed comparison in the JSON has to break a test.
/// </summary>
public sealed class SubjectRatingTests
{
	// The accessible tier, deliberately rated green at a 4.0 average so a borderline-eligible student has a
	// programme to enrol on. Every other subject stays red there.
	private static readonly HashSet<Subject> AccessibleSubjects =
		[Subject.Psychology, Subject.Sociology, Subject.MediaStudies];

	private static IReadOnlyList<SubjectRating> Rate(params (string Subject, int Grade)[] gcses)
	{
		var student = new StudentInput("S-TEST", gcses.ToDictionary(g => g.Subject, g => g.Grade), []);
		var evaluator = Harness.ShippedEvaluator();
		return evaluator.EvaluateRatings(Harness.Predict(student), student.ToGcseResults());
	}

	private static Rating Of(IEnumerable<SubjectRating> ratings, Subject subject) =>
		ratings.Single(r => r.Subject == subject).Rating;

	// Rate one subject from a hand-built profile: predicted points and DfE evidence are supplied directly,
	// bypassing prediction, so a tier boundary can be probed without hunting for a GCSE set that lands the
	// average on the exact predicted value. The gcses still drive the entry gate through the engine.
	private static Rating RateSynthetic(
		Subject subject, double predictedPoints, TransitionEvidence evidence, params (string Subject, int Grade)[] gcses)
	{
		var profile = new StudentProfile("S-TEST", 6.0, [new(subject, predictedPoints)], [evidence], []);
		var ratings = Harness.ShippedEvaluator()
			.EvaluateRatings(profile, [.. gcses.Select(g => new GcseResult(g.Subject, g.Grade))]);
		return ratings.Single(r => r.Subject == subject).Rating;
	}

	// All probability mass at A*, so P(≥ any grade) = 1 and neither DfE confidence floor ever blocks: the
	// rating then turns purely on the predicted-points tier.
	private static TransitionEvidence FullConfidence(Subject subject) =>
		new(subject, "test", "synthetic", 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0);

	// A full set of GCSEs at one uniform grade, so the average equals that grade.
	private static (string, int)[] Uniform(int grade) => [
		("maths", grade), ("english_language", grade), ("physics", grade),
		("chemistry", grade), ("biology", grade), ("english_literature", grade),
		("french", grade), ("german", grade), ("physical_education", grade),
		("computer_studies", grade), ("history", grade), ("music", grade), ("art", grade),
	];

	[Fact]
	public void top_student_is_green_in_every_subject()
	{
		// Average 9.0: every entry requirement is met and every predicted grade clears its green threshold.
		var ratings = Rate(Uniform(9));

		ratings.Should().OnlyContain(r => r.Rating == Rating.Green);
	}

	[Fact]
	public void weak_student_is_red_in_every_subject_outside_the_accessible_tier()
	{
		// Average 4.0: every entry requirement fails (supporting GCSEs and the average are all too low)
		// except in the accessible tier, which is tuned to open at pass_grade — see AccessibleSubjectsTests.
		// Physical Education's entry gate also opens at pass_grade (biology/psychology/PE GCSE), but its
		// regression was not retuned for the accessible tier, so a 4.0 average rates it amber, not green.
		var ratings = Rate(Uniform(4));
		var outsideAccessibleTier = AccessibleSubjects.Append(Subject.PhysicalEducation);

		ratings.Where(r => !outsideAccessibleTier.Contains(r.Subject))
			.Should().OnlyContain(r => r.Rating == Rating.Red);
		ratings.Where(r => AccessibleSubjects.Contains(r.Subject))
			.Should().OnlyContain(r => r.Rating == Rating.Green);
		Of(ratings, Subject.PhysicalEducation).Should().Be(Rating.Amber);
	}

	[Fact]
	public void exactly_one_rating_per_subject()
	{
		var ratings = Rate(Uniform(6));

		ratings.Select(r => r.Subject).Should().BeEquivalentTo(Catalogue.Subjects);
		ratings.Should().HaveCount(Catalogue.Subjects.Count);
	}

	[Fact]
	public void english_lit_entry_requires_an_english_gcse_at_standard_entry()
	{
		// No English GCSE at standard entry (English Language one below): entry fails ⇒ red regardless of prediction.
		var below = Rate(("english_language", Harness.Thresholds.StandardEntry - 1), ("maths", Harness.Thresholds.StandardEntry));
		Of(below, Subject.EnglishLiterature).Should().Be(Rating.Red);

		// English Language at standard entry satisfies the either-English entry ⇒ no longer barred at the gate.
		var met = Rate(("english_language", Harness.Thresholds.StandardEntry), ("maths", Harness.Thresholds.StandardEntry));
		Of(met, Subject.EnglishLiterature).Should().NotBe(Rating.Red);
	}

	[Fact]
	public void maths_entry_requires_maths_gcse_at_the_exceptional_grade()
	{
		// Maths GCSE one below the exceptional grade ⇒ the hard entry gate fails ⇒ red however strong the prediction.
		Of(Rate(("maths", Harness.Thresholds.ExceptionalEntry - 1), ("art", 9)), Subject.Maths).Should().Be(Rating.Red);

		// Maths GCSE at the exceptional grade with a strong all-round profile ⇒ entry met and the prediction
		// clears the green tier.
		Of(Rate(("maths", Harness.Thresholds.ExceptionalEntry), ("art", 9)), Subject.Maths).Should().Be(Rating.Green);
	}

	[Fact]
	public void tier_boundary_straddles_green_amber_and_red()
	{
		var confident = FullConfidence(Subject.Music);

		// Entry (Music ≥ standard) met throughout; only the predicted points move across the tier lines.
		// Predicted D clears the green tier; predicted E clears only amber; below E is red.
		RateSynthetic(Subject.Music, ALevelGrade.D, confident, ("music", Harness.Thresholds.StandardEntry)).Should().Be(Rating.Green);
		RateSynthetic(Subject.Music, ALevelGrade.E, confident, ("music", Harness.Thresholds.StandardEntry)).Should().Be(Rating.Amber);
		RateSynthetic(Subject.Music, ALevelGrade.E - 0.5, confident, ("music", Harness.Thresholds.StandardEntry)).Should().Be(Rating.Red);
	}

	[Fact]
	public void green_dfe_confidence_floor_demotes_a_points_eligible_subject_to_amber()
	{
		// Predicted D clears the green point tier, but P(≥D) = 0.55 sits between the amber (0.50) and green
		// (0.60) DfE confidence floors, so green is blocked on confidence and the subject is amber. Reverting
		// the green floor to the amber floor would flip this back to green and break here.
		var midConfidence = new TransitionEvidence(Subject.Music, "test", "synthetic", 0.35, 0.10, 0.55, 0.0, 0.0, 0.0, 0.0);

		RateSynthetic(Subject.Music, ALevelGrade.D, midConfidence, ("music", Harness.Thresholds.StandardEntry))
			.Should().Be(Rating.Amber);
	}

	[Fact]
	public void maths_green_rule_requires_dfe_transition_probability_evidence()
	{
		var profile = new StudentProfile(
			"S-TEST",
			7.5,
			[new(Subject.Maths, ALevelGrade.A)],
			[],
			[]);
		var evaluator = Harness.ShippedEvaluator();

		var ratings = evaluator.EvaluateRatings(profile, [new("maths", Harness.Thresholds.ExceptionalEntry)]);

		// The point prediction clears green and amber, but both tiers now consume DfE probability evidence.
		// With no matrix row on the profile, both tiers are blocked and red wins.
		Of(ratings, Subject.Maths).Should().Be(Rating.Red);
	}

	[Fact]
	public void science_green_rules_require_dfe_transition_probability_evidence()
	{
		var profile = new StudentProfile(
			"S-TEST",
			8.0,
			[new(Subject.Physics, ALevelGrade.B)],
			[],
			[]);
		var evaluator = Harness.ShippedEvaluator();

		var ratings = evaluator.EvaluateRatings(
			profile,
			[new("maths", Harness.Thresholds.ExceptionalEntry), new("physics", Harness.Thresholds.StrongEntry)]);

		// Physics clears entry and the predicted B green tier, but both green and amber are blocked without the DfE row.
		Of(ratings, Subject.Physics).Should().Be(Rating.Red);
	}

	[Fact]
	public void amber_rules_require_dfe_transition_probability_evidence()
	{
		var profile = new StudentProfile(
			"S-TEST",
			6.0,
			[new(Subject.History, ALevelGrade.C)],
			[],
			[]);
		var evaluator = Harness.ShippedEvaluator();

		var ratings = evaluator.EvaluateRatings(profile, []);

		// History clears average entry and the predicted C amber tier, but amber is blocked without DfE evidence.
		Of(ratings, Subject.History).Should().Be(Rating.Red);
	}

	[Fact]
	public void english_lit_entry_is_satisfied_by_either_english_gcse()
	{
		// A student who sat English Language but not English Literature GCSE still clears English Literature
		// entry through the either-English rule, and a strong profile rates it green.
		var viaLanguage = Rate(("english_language", 7), ("maths", 7));
		Of(viaLanguage, Subject.EnglishLiterature).Should().Be(Rating.Green);

		// Equally, the English Literature GCSE alone satisfies entry.
		var viaLiterature = Rate(("english_literature", 7), ("maths", 7));
		Of(viaLiterature, Subject.EnglishLiterature).Should().Be(Rating.Green);
	}

	[Fact]
	public void physical_education_entry_is_satisfied_by_biology_psychology_or_pe_at_pass_grade()
	{
		// A student who sat Biology but not PE GCSE still clears PE entry through the either-of rule.
		var viaBiology = Rate(("biology", 7), ("maths", 7), ("english_language", 7));
		Of(viaBiology, Subject.PhysicalEducation).Should().Be(Rating.Green);

		// Equally, Psychology GCSE alone satisfies entry.
		var viaPsychology = Rate(("psychology", 7), ("maths", 7), ("english_language", 7));
		Of(viaPsychology, Subject.PhysicalEducation).Should().Be(Rating.Green);

		// PE GCSE itself still satisfies entry.
		var viaPe = Rate(("physical_education", 7), ("maths", 7), ("english_language", 7));
		Of(viaPe, Subject.PhysicalEducation).Should().Be(Rating.Green);

		// None of the three, at pass grade only: entry unmet ⇒ red regardless of average.
		var viaNone = Rate(("maths", 7), ("english_language", 7), ("history", 7));
		Of(viaNone, Subject.PhysicalEducation).Should().Be(Rating.Red);

		// Grade 4 (pass grade) in any of the three is enough — this is meant to be an accessible course.
		var atPassGrade = Rate(("biology", 4), ("maths", 4), ("english_language", 4), ("history", 4), ("art", 4));
		Of(atPassGrade, Subject.PhysicalEducation).Should().NotBe(Rating.Red);
	}

	[Fact]
	public void missing_supporting_subject_fails_entry_to_red()
	{
		// Strong Maths but no Physics GCSE: Maths is green, Physics fails its supporting-GCSE entry ⇒ red.
		var ratings = Rate(("maths", 9));

		Of(ratings, Subject.Maths).Should().Be(Rating.Green);
		Of(ratings, Subject.Physics).Should().Be(Rating.Red);
	}

	[Fact]
	public void every_rating_carries_a_reason()
	{
		var ratings = Rate(Uniform(6));

		ratings.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Reason));
	}

	[Fact]
	public void shipped_subject_ratings_workflow_probe_compiles()
	{
		var (workflows, engine) = Harness.BuildFromShippedWorkflows();

		var act = () => WorkflowStore.ProbeCompile(engine, workflows, Harness.CanonicalProbe());

		act.Should().NotThrow();
	}
}
