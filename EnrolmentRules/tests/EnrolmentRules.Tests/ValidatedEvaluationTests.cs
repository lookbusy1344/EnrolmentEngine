namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Pre-v1 integration hardening — <c>Try*</c> evaluation validates at the engine boundary before
///     entering the pipeline.
/// </summary>
public sealed class ValidatedEvaluationTests
{
	private static readonly DateOnly ValidDob = new(2009, 9, 1);

	private readonly EnrolmentEngine engine = Harness.ShippedEngine();

	private static StudentInput StudentWithMathsGrade(int mathsGrade) =>
		new(
			"S-BAD",
			new Dictionary<string, int> {
				["english_language"] = 6,
				["maths"] = mathsGrade,
				["physics"] = 6,
				["chemistry"] = 6,
				["biology"] = 6,
			},
			[]) { DateOfBirth = ValidDob };

	private static StudentInput EligibleStudent() => StudentWithMathsGrade(6);

	/// <summary>
	///     The <c>examples/golden/strong-constraints.json</c> facts: eligible, a stable mix of ratings, and
	///     the fixture the web suite drives through both front ends. Music rates green on its own tier but is
	///     demoted to amber by its unmet own-time requirement ("chess_club" is not a "plays_" activity), and
	///     Further Maths rates red — the two cases the stale-choice guard has to tell apart.
	/// </summary>
	private static StudentInput StrongStudent() =>
		new(
			"S-STRONG",
			new Dictionary<string, int> {
				["maths"] = 8,
				["english_language"] = 8,
				["english_literature"] = 8,
				["physics"] = 8,
				["chemistry"] = 8,
				["biology"] = 8,
				["french"] = 8,
				["german"] = 8,
				["physical_education"] = 8,
				["computer_studies"] = 8,
				["history"] = 8,
				["music"] = 8,
				["art"] = 8,
			},
			["chess_club"]) { DateOfBirth = ValidDob };

	private static CatalogueData MathsOnlyCatalogue() =>
		new(
			new Dictionary<Subject, SubjectMeta> { [Subject.Maths] = Harness.Catalogue.Meta(Subject.Maths) },
			[Subject.Maths]);

	private static void ShouldRejectNull(Action action) =>
		action.Should().Throw<ArgumentNullException>().WithParameterName("student");

	[Fact]
	public void evaluate_does_not_validate_out_of_range_grades()
	{
		var student = StudentWithMathsGrade(99);

		var result = engine.Evaluate(student);

		result.Recommendations.Should().NotBeEmpty();
	}

	[Fact]
	public void evaluate_fails_loud_when_chosen_subject_is_outside_the_bound_catalogue()
	{
		var (_, rulesEngine) = Harness.BuildFromShippedWorkflows();
		var limitedEngine = new EnrolmentEngine(rulesEngine, Harness.Thresholds, MathsOnlyCatalogue(), Harness.AsOf, Harness.Scale);
		var student = new StudentInput(
			"S-BAD",
			new Dictionary<string, int> { ["english_language"] = 6, ["maths"] = 6 },
			[]) { DateOfBirth = ValidDob, ChosenALevels = [Subject.Physics] };

		var act = () => limitedEngine.Evaluate(student);

		act.Should().Throw<CatalogueDataException>()
			.WithMessage("*physics*bound catalogue*");
	}

	[Fact]
	public void evaluate_rejects_null_input_at_the_boundary()
	{
		StudentInput? student = null;

		ShouldRejectNull(() => engine.Evaluate(student!));
		ShouldRejectNull(() => engine.Evaluate(student!, Harness.AsOf));
	}

	[Fact]
	public void try_evaluate_rejects_out_of_range_grades_without_running_the_pipeline()
	{
		var student = StudentWithMathsGrade(99);

		var outcome = engine.TryEvaluate(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle()
			.Which.Should().Contain("maths").And.Contain("out of range");
	}

	[Fact]
	public void try_evaluate_rejects_null_input_as_structured_validation()
	{
		StudentInput? student = null;

		var outcome = engine.TryEvaluate(student!);
		var datedOutcome = engine.TryEvaluate(student!, Harness.AsOf);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
		datedOutcome.Validation.IsValid.Should().BeFalse();
		datedOutcome.Value.Should().BeNull();
		datedOutcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
	}

	[Fact]
	public void try_evaluate_rejects_a_missing_date_of_birth()
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = 6 }, []);

		var outcome = engine.TryEvaluate(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle()
			.Which.Should().Contain("date_of_birth");
	}

	[Fact]
	public void try_evaluate_matches_evaluate_for_a_valid_student()
	{
		var student = EligibleStudent();

		var tryOutcome = engine.TryEvaluate(student);
		var direct = engine.Evaluate(student);

		tryOutcome.Validation.IsValid.Should().BeTrue();
		tryOutcome.Value.Should().BeEquivalentTo(direct);
	}

	// ---- stale committed choices (a choice that was green when made, and has since gone red) --------

	[Fact]
	public void try_evaluate_rejects_a_chosen_a_level_the_pipeline_now_rates_red()
	{
		// Further Maths requires Maths as a committed choice; choosing it alone leaves the prerequisite
		// unmet, so it rates red — the shape of a choice whose facts moved under it after it was made.
		var student = EligibleStudent() with { ChosenALevels = [Subject.FurtherMaths] };

		var outcome = engine.TryEvaluate(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle()
			.Which.Should().Contain("chosen_a_levels").And.Contain("further_maths");
	}

	[Fact]
	public void a_rejected_chosen_a_level_reports_the_reason_it_went_red()
	{
		var student = StrongStudent() with { ChosenALevels = [Subject.FurtherMaths] };

		var outcome = engine.TryExplain(student);

		outcome.Validation.Errors.Should().ContainSingle()
			.Which.Should().Contain(ConstraintPass.MathsPrerequisiteReason);
	}

	[Fact]
	public void try_explain_rejects_a_chosen_a_level_the_pipeline_now_rates_red()
	{
		var student = EligibleStudent() with { ChosenALevels = [Subject.FurtherMaths] };

		var outcome = engine.TryExplain(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
	}

	[Fact]
	public void an_ineligible_student_has_every_chosen_a_level_rejected()
	{
		// The gate reds every subject, so nothing the student committed to survives.
		var student = new StudentInput("S-INELIGIBLE", new Dictionary<string, int> { ["maths"] = 6 }, []) {
			DateOfBirth = ValidDob,
			ChosenALevels = [Subject.Maths, Subject.Physics],
		};

		var outcome = engine.TryEvaluate(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().HaveCount(2);
	}

	[Fact]
	public void an_amber_chosen_a_level_is_still_accepted()
	{
		// Only red is rejected: Music's own-time requirement demotes it to amber for a student with no
		// "plays_" hobby, and an amber choice remains a choice the student may hold.
		var student = StrongStudent() with { ChosenALevels = [Subject.Music] };

		var outcome = engine.TryExplain(student);

		outcome.Validation.IsValid.Should().BeTrue();
		outcome.Value!.Explanations.Single(e => e.Subject == Subject.Music).Rating.Should().Be(Rating.Amber);
	}

	[Fact]
	public void try_advise_still_advises_a_student_holding_a_now_red_choice()
	{
		// The deliberate exemption: advice exists to say what would have to change, so a red committed
		// choice is the case that most needs an answer rather than a refusal. TryEvaluate/TryExplain
		// refuse the same document — nothing is accepted by advising on it.
		var student = EligibleStudent() with { ChosenALevels = [Subject.FurtherMaths] };

		var outcome = engine.TryAdvise(student);

		outcome.Validation.IsValid.Should().BeTrue();
		outcome.Value.Should().NotBeNull();
		engine.TryExplain(student).Value.Should().BeNull();
	}

	[Fact]
	public void explain_still_reports_a_red_chosen_a_level_without_validating_it()
	{
		// The unchecked path is unchanged: rejection is a Try* boundary concern, not a pipeline one.
		var student = EligibleStudent() with { ChosenALevels = [Subject.FurtherMaths] };

		var result = engine.Explain(student);

		result.Explanations.Single(e => e.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Red);
	}

	[Fact]
	public void try_explain_rejects_invalid_input()
	{
		var student = StudentWithMathsGrade(99);

		var outcome = engine.TryExplain(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
	}

	[Fact]
	public void explain_rejects_null_input_at_the_boundary()
	{
		StudentInput? student = null;

		ShouldRejectNull(() => engine.Explain(student!));
		ShouldRejectNull(() => engine.Explain(student!, Harness.AsOf));
	}

	[Fact]
	public void try_explain_rejects_null_input_as_structured_validation()
	{
		StudentInput? student = null;

		var outcome = engine.TryExplain(student!);
		var datedOutcome = engine.TryExplain(student!, Harness.AsOf);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
		datedOutcome.Validation.IsValid.Should().BeFalse();
		datedOutcome.Value.Should().BeNull();
		datedOutcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
	}

	[Fact]
	public void try_explain_matches_explain_for_a_valid_student()
	{
		var student = EligibleStudent();

		var tryOutcome = engine.TryExplain(student);
		var direct = engine.Explain(student);

		tryOutcome.Validation.IsValid.Should().BeTrue();
		tryOutcome.Value.Should().BeEquivalentTo(direct);
	}

	[Fact]
	public void try_advise_rejects_invalid_input()
	{
		var student = StudentWithMathsGrade(99);

		var outcome = engine.TryAdvise(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
	}

	[Fact]
	public void advise_rejects_null_input_at_the_boundary()
	{
		StudentInput? student = null;
		var throwingClockEngine = new EnrolmentEngine(
			Harness.ShippedEvaluator(),
			Harness.Catalogue,
			static () => throw new InvalidOperationException("The clock must not be resolved for invalid input."));

		ShouldRejectNull(() => engine.Advise(student!));
		ShouldRejectNull(() => engine.Advise(student!, Harness.AsOf));
		ShouldRejectNull(() => throwingClockEngine.Advise(student!, true));
		ShouldRejectNull(() => engine.Advise(student!, Harness.AsOf, true));
	}

	[Fact]
	public void try_advise_rejects_null_input_as_structured_validation()
	{
		StudentInput? student = null;

		var outcome = engine.TryAdvise(student!);
		var datedOutcome = engine.TryAdvise(student!, Harness.AsOf);
		var toggledOutcome = engine.TryAdvise(student!, true);
		var datedToggledOutcome = engine.TryAdvise(student!, Harness.AsOf, true);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
		datedOutcome.Validation.IsValid.Should().BeFalse();
		datedOutcome.Value.Should().BeNull();
		datedOutcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
		toggledOutcome.Validation.IsValid.Should().BeFalse();
		toggledOutcome.Value.Should().BeNull();
		toggledOutcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
		datedToggledOutcome.Validation.IsValid.Should().BeFalse();
		datedToggledOutcome.Value.Should().BeNull();
		datedToggledOutcome.Validation.Errors.Should().ContainSingle().Which.Should().Be("student is required");
	}

	[Fact]
	public void try_advise_matches_advise_for_a_valid_student()
	{
		var student = EligibleStudent();

		var tryOutcome = engine.TryAdvise(student);
		var direct = engine.Advise(student);

		tryOutcome.Validation.IsValid.Should().BeTrue();
		tryOutcome.Value.Should().BeEquivalentTo(direct);
	}
}
