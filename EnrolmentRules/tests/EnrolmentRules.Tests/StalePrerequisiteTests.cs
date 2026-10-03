namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

public sealed class StalePrerequisiteTests
{
	private const int StudentAgeYears = 16;

	[Fact]
	public void a_red_maths_choice_makes_further_maths_unavailable_in_policy_comparisons()
	{
		var registry = new EnrolmentPolicyRegistry(
			[new(new("standard"), "Standard", new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir))],
			new("standard"), static () => Harness.AsOf);

		var comparison = registry.Compare(new("standard"), StudentWithStaleMaths());

		comparison.Validation.IsValid.Should().BeTrue();
		comparison.Value!.ChoiceStatuses.Should().HaveCount(2)
				  .And.OnlyContain(static status => status.Status == ChoiceStatus.Unavailable);
	}

	[Fact]
	public void stale_choices_include_dependants_and_pruning_once_leaves_a_valid_basket()
	{
		var engine = Harness.ShippedEngine();
		var student = StudentWithStaleMaths();

		var stale = engine.StaleChoices(student);

		stale.Should().BeEquivalentTo([Subject.Maths, Subject.FurtherMaths]);
		var pruned = student with {
			ChosenALevels = EquatableArray.CopyOf(student.ChosenALevels.Except(stale)),
		};
		engine.StaleChoices(pruned).Should().BeEmpty();
		engine.EvaluateValidated(pruned).Validation.IsValid.Should().BeTrue();
	}

	[Fact]
	public void validated_evaluation_rejects_both_a_red_prerequisite_and_its_chosen_dependant()
	{
		var evaluation = Harness.ShippedEngine().EvaluateValidated(StudentWithStaleMaths());

		evaluation.Value.Should().BeNull();
		evaluation.Validation.Errors.Should().HaveCount(2)
				  .And.Contain(error => error.Contains("'maths'", StringComparison.Ordinal))
				  .And.Contain(error => error.Contains("'further_maths'", StringComparison.Ordinal));
	}

	private static StudentInput StudentWithStaleMaths() => new("stale-prerequisite", new Dictionary<string, int> {
		["maths"] = Harness.Thresholds.TopEntry,
		["english_language"] = Thresholds.MaxGcseGrade,
		["physics"] = Thresholds.MaxGcseGrade,
		["chemistry"] = Thresholds.MaxGcseGrade,
		["biology"] = Thresholds.MaxGcseGrade,
	}, []) {
		DateOfBirth = Harness.AsOf.AddYears(-StudentAgeYears),
		ChosenALevels = [Subject.Maths, Subject.FurtherMaths],
	};
}
