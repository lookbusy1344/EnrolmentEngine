namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;

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

	private static CatalogueData MathsOnlyCatalogue() =>
		new(
			new Dictionary<Subject, SubjectMeta> { [Subject.Maths] = Harness.Catalogue.Meta(Subject.Maths) },
			[Subject.Maths]);

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

	[Fact]
	public void try_explain_rejects_invalid_input()
	{
		var student = StudentWithMathsGrade(99);

		var outcome = engine.TryExplain(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
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
	public void try_advise_matches_advise_for_a_valid_student()
	{
		var student = EligibleStudent();

		var tryOutcome = engine.TryAdvise(student);
		var direct = engine.Advise(student);

		tryOutcome.Validation.IsValid.Should().BeTrue();
		tryOutcome.Value.Should().BeEquivalentTo(direct);
	}
}
