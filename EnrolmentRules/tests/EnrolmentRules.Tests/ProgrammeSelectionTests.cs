namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 1.5 — the final-programme boundary
///     (<see cref="EnrolmentEngine.ValidateFinalProgramme(StudentInput, System.Threading.CancellationToken)" />),
///     distinct from <see cref="StudentValidator" />'s incremental basket checks: the basket stays freely
///     editable (0, 1 or 2 choices are fine mid-edit), but a caller finalising a programme needs an
///     explicit minimum/maximum/red-choice gate. Standard leaves <see cref="PolicyThresholds.MinChosenALevels" />
///     at its zero default, so this exercises the machinery mostly against an Elite-shaped override.
/// </summary>
public sealed class ProgrammeSelectionTests
{
	private static StudentInput LoadFixture(string fixture)
	{
		using var stream = File.OpenRead(Path.Combine(Harness.RepoRoot, "examples", "golden", fixture + ".json"));
		return JsonSerializer.Deserialize(stream, EnrolmentJsonContext.Default.StudentDocument)!.Student;
	}

	[Fact]
	public void standard_defaults_preserve_incremental_behaviour_for_an_empty_basket()
	{
		var engine = Harness.ShippedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.Subjects.Should().BeEmpty();
		result.Value.MinRequired.Should().Be(0);
	}

	[Fact]
	public void fewer_than_the_configured_minimum_is_invalid_for_finalisation()
	{
		var engine = EliteShapedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [Subject.Art, Subject.Music],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeFalse();
		result.Value.Should().BeNull();
		result.Validation.Errors.Should().Contain(e => e.Contains("at least 3", StringComparison.Ordinal));
	}

	[Fact]
	public void exactly_the_minimum_passes()
	{
		var engine = EliteShapedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [Subject.Art, Subject.Music, Subject.History],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeTrue();
		result.Value!.Subjects.Should().HaveCount(3);
	}

	[Fact]
	public void exactly_the_maximum_passes()
	{
		var engine = EliteShapedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [Subject.Art, Subject.Music, Subject.History, Subject.Biology],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeTrue();
		result.Value!.Subjects.Should().HaveCount(4);
		result.Value.MaxAllowed.Should().Be(4);
	}

	[Fact]
	public void more_than_the_effective_maximum_fails()
	{
		var engine = EliteShapedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [Subject.Art, Subject.Music, Subject.History, Subject.Biology, Subject.Chemistry],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeFalse();
		result.Validation.Errors.Should().Contain(e => e.Contains("at most 4", StringComparison.Ordinal));
	}

	[Fact]
	public void a_duplicate_chosen_subject_fails_via_the_shared_structural_validation()
	{
		var engine = EliteShapedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [Subject.Art, Subject.Art, Subject.Music],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeFalse();
		result.Validation.Errors.Should().Contain(e => e.Contains("duplicates", StringComparison.Ordinal));
	}

	[Fact]
	public void a_not_offered_chosen_subject_fails_via_the_shared_structural_validation()
	{
		var engine = EliteShapedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [new("not_a_real_subject")],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeFalse();
		result.Validation.Errors.Should().Contain(e => e.Contains("invalid", StringComparison.Ordinal));
	}

	[Fact]
	public void a_red_chosen_subject_fails_strict_finalisation()
	{
		// French <-> German is a hard (red) clash in the shipped catalogue; committing to both makes one
		// of them a red chosen_a_levels entry, which strict finalisation must refuse just like
		// EvaluateValidated's stale-choice guard does.
		var engine = Harness.ShippedEngine();
		var student = LoadFixture("strong-constraints") with {
			ChosenALevels = [Subject.French, Subject.German],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeFalse();
		result.Validation.Errors.Should().Contain(e => e.Contains("no longer available", StringComparison.Ordinal));
	}

	[Fact]
	public void an_amber_chosen_subject_remains_permitted()
	{
		var engine = Harness.ShippedEngine();
		var fixture = LoadFixture("strong-constraints");
		var explained = engine.Explain(fixture);
		var amberSubject = explained.Explanations.First(e => e.Rating == Rating.Amber).Subject;
		var student = fixture with {
			ChosenALevels = [amberSubject],
		};

		var result = engine.ValidateFinalProgramme(student);

		result.Validation.IsValid.Should().BeTrue();
		result.Value!.Subjects.Should().Equal(amberSubject);
	}

	private static EnrolmentEngine EliteShapedEngine()
	{
		var (workflows, rules) = Harness.BuildFromShippedWorkflows();
		var thresholds = Harness.Thresholds with {
			MinChosenALevels = 3,
		};
		return new(rules, thresholds, Harness.Catalogue, Harness.AsOf, Harness.Scale, workflows);
	}
}
