namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;

/// <summary>
///     Pre-v1 integration hardening — <c>Try*</c> evaluation validates at the engine boundary before
///     entering the pipeline.
/// </summary>
public sealed class ValidatedEvaluationTests : IAsyncLifetime
{
	private static readonly DateOnly ValidDob = new(2009, 9, 1);

	private EnrolmentEngine engine = null!;

	public async Task InitializeAsync() => engine = await Harness.ShippedEngineAsync();

	public Task DisposeAsync() => Task.CompletedTask;

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

	[Fact]
	public async Task evaluate_async_does_not_validate_out_of_range_grades()
	{
		var student = StudentWithMathsGrade(99);

		var result = await engine.EvaluateAsync(student);

		result.Recommendations.Should().NotBeEmpty();
	}

	[Fact]
	public async Task try_evaluate_async_rejects_out_of_range_grades_without_running_the_pipeline()
	{
		var student = StudentWithMathsGrade(99);

		var outcome = await engine.TryEvaluateAsync(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle()
			.Which.Should().Contain("maths").And.Contain("out of range");
	}

	[Fact]
	public async Task try_evaluate_async_rejects_a_missing_date_of_birth()
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = 6 }, []);

		var outcome = await engine.TryEvaluateAsync(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle()
			.Which.Should().Contain("date_of_birth");
	}

	[Fact]
	public async Task try_evaluate_async_matches_evaluate_async_for_a_valid_student()
	{
		var student = EligibleStudent();

		var tryOutcome = await engine.TryEvaluateAsync(student);
		var direct = await engine.EvaluateAsync(student);

		tryOutcome.Validation.IsValid.Should().BeTrue();
		tryOutcome.Value.Should().BeEquivalentTo(direct);
	}

	[Fact]
	public async Task try_explain_async_rejects_invalid_input()
	{
		var student = StudentWithMathsGrade(99);

		var outcome = await engine.TryExplainAsync(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
	}

	[Fact]
	public async Task try_explain_async_matches_explain_async_for_a_valid_student()
	{
		var student = EligibleStudent();

		var tryOutcome = await engine.TryExplainAsync(student);
		var direct = await engine.ExplainAsync(student);

		tryOutcome.Validation.IsValid.Should().BeTrue();
		tryOutcome.Value.Should().BeEquivalentTo(direct);
	}

	[Fact]
	public async Task try_advise_async_rejects_invalid_input()
	{
		var student = StudentWithMathsGrade(99);

		var outcome = await engine.TryAdviseAsync(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
	}

	[Fact]
	public async Task try_advise_async_matches_advise_async_for_a_valid_student()
	{
		var student = EligibleStudent();

		var tryOutcome = await engine.TryAdviseAsync(student);
		var direct = await engine.AdviseAsync(student);

		tryOutcome.Validation.IsValid.Should().BeTrue();
		tryOutcome.Value.Should().BeEquivalentTo(direct);
	}
}
