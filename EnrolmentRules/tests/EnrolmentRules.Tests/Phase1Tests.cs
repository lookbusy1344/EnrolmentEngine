namespace EnrolmentRules.Tests;

using System.Text.Json;
using Cli;
using Domain;
using Engine;
using FluentAssertions;
using Prediction;

/// <summary>
///     Phase 1 — fact ingestion + the statistical prediction stage (host code, upstream of the engine)
///     + the runnable CLI. Pins the average and the per-subject regression against an independent
///     recomputation from the shipped catalogue coefficients (never a literal).
/// </summary>
public sealed class Phase1Tests
{
	private const double Tolerance = 1e-9;
	private static string DataDir => Path.Combine(Harness.RepoRoot, "data");
	private static CatalogueData ShippedCatalogue => CatalogueStore.LoadAndValidate(DataDir);
	public static TheoryData<string> ShippedSubjects { get; } = BuildSubjects();

	private static StudentInput Student(Dictionary<string, int> gcses, params string[] hobbies) =>
		new("S-TEST", gcses, hobbies);

	private static TheoryData<string> BuildSubjects()
	{
		var data = new TheoryData<string>();
		foreach (var subject in ShippedCatalogue.Subjects) {
			data.Add(subject.Value);
		}

		return data;
	}

	// Phase 1 exercises GCSE averaging/prediction, not the age gate, so a fixed reference date suffices.
	private static StudentProfile Predict(StudentInput student) => Harness.Predict(student);

	private static CatalogueData FurtherMathsPrerequisiteCatalogue(bool requiresMaths)
	{
		var shipped = ShippedCatalogue;
		var entries = shipped.Subjects.ToDictionary(
			static subject => subject,
			subject => {
				var meta = shipped.Meta(subject);
				return subject == Subject.FurtherMaths
					? meta with { Prerequisites = requiresMaths ? [new([Subject.Maths], Rating.Red, PrerequisiteSatisfaction.Chosen)] : [] }
					: meta;
			});

		return new(entries, shipped.Subjects);
	}

	private static CatalogueData LimitedCatalogue() =>
		new(
			new Dictionary<Subject, SubjectMeta> {
				[Subject.Maths] = new(
					3,
					new(0.0, 0.0),
					[],
					[],
					[],
					[]),
			},
			[Subject.Maths]);

	[Fact]
	public void average_is_the_exact_mean_over_present_gcses()
	{
		// Absent subjects simply are not in the map, so they cannot affect the mean.
		var gcses = new Dictionary<string, int> { ["maths"] = 9, ["physics"] = 6, ["history"] = 3 };

		var profile = Predict(Student(gcses));

		profile.AverageGcseScore.Should().BeApproximately((9 + 6 + 3) / 3.0, Tolerance);
	}

	[Fact]
	public void absent_subjects_are_excluded_not_counted_as_zero()
	{
		// Two present subjects average 6.0. If an absent subject were treated as a 0 grade the
		// mean would collapse toward zero — assert it does not.
		var profile = Predict(Student(new() { ["maths"] = 8, ["art"] = 4 }));

		profile.AverageGcseScore.Should().BeApproximately(6.0, Tolerance);
	}

	[Theory]
	[MemberData(nameof(ShippedSubjects))]
	public void predicted_grade_matches_independent_recomputation_from_named_coefficients(string subjectName)
	{
		Subject.TryParse(subjectName, out var subject).Should().BeTrue();
		var gcses = new Dictionary<string, int> { ["maths"] = 7, ["physics"] = 6, ["english_language"] = 5 };
		var average = (7 + 6 + 5) / 3.0;

		var profile = Predict(Student(gcses));

		var coefficients = ShippedCatalogue.Meta(subject).Regression;
		var expected = coefficients.Predict(average);

		var predicted = profile.PredictedGrades.Single(p => p.Subject == subject).PredictedPoints;
		predicted.Should().BeApproximately(expected, Tolerance);
	}

	[Fact]
	public void tweaked_catalogue_regression_changes_the_prediction()
	{
		var student = Student(new() { ["maths"] = 7, ["physics"] = 6, ["english_language"] = 5 });
		var gcses = student.ToGcseResults();
		var average = (7 + 6 + 5) / 3.0;

		var shipped = CatalogueStore.LoadAndValidate(DataDir);
		var tweaked = new CatalogueData(
			Catalogue.Subjects.ToDictionary(
				static subject => subject,
				subject => subject == Subject.Maths
					? shipped.Meta(subject) with { Regression = new(0.50, 0.25) }
					: shipped.Meta(subject)));

		var profile = GradePredictor.Predict(student, gcses, Harness.AsOf, tweaked, Harness.Scale);

		var predicted = profile.PredictedGrades.Single(p => p.Subject == Subject.Maths).PredictedPoints;
		predicted.Should().BeApproximately(Math.Clamp((0.50 * average) + 0.25, ALevelGrade.Min, ALevelGrade.Max), Tolerance);
		predicted.Should().NotBeApproximately(shipped.Meta(Subject.Maths).Regression.Predict(average), Tolerance);
	}

	[Fact]
	public async Task two_engines_with_different_catalogues_can_disagree_without_global_state()
	{
		var student = Student(new() {
			["english_language"] = 9,
			["maths"] = 9,
			["physics"] = 9,
			["chemistry"] = 9,
			["biology"] = 9,
			["english_literature"] = 9,
			["french"] = 9,
			["german"] = 9,
			["physical_education"] = 9,
			["computer_studies"] = 9,
			["history"] = 9,
			["music"] = 9,
			["art"] = 9,
		});
		student = student with { ChosenALevels = [Subject.FurtherMaths] };

		var (workflows, rulesEngine) = await Harness.BuildFromShippedWorkflowsAsync();
		_ = workflows;

		var withPrerequisite = new EnrolmentEngine(rulesEngine, Harness.Thresholds, FurtherMathsPrerequisiteCatalogue(true), Harness.AsOf);
		var withoutPrerequisite = new EnrolmentEngine(rulesEngine, Harness.Thresholds, FurtherMathsPrerequisiteCatalogue(false), Harness.AsOf);

		var constrained = await withPrerequisite.EvaluateAsync(student);
		var unconstrained = await withoutPrerequisite.EvaluateAsync(student);

		constrained.Recommendations.Single(r => r.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Red);
		unconstrained.Recommendations.Single(r => r.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Green);
	}

	[Fact]
	public void validator_uses_the_catalogue_passed_to_it()
	{
		var student = Student(new() { ["maths"] = 6 }, "plays_piano") with { ChosenALevels = [Subject.Physics], DateOfBirth = new(2009, 9, 1) };

		StudentValidator.Validate(student, LimitedCatalogue(), Harness.Scale)
			.Should()
			.ContainSingle()
			.Which.Should().Contain("physics");
	}

	[Fact]
	public void every_subject_receives_exactly_one_prediction()
	{
		var profile = Predict(Student(new() { ["maths"] = 6 }));

		profile.PredictedGrades.Select(p => p.Subject)
			.Should().BeEquivalentTo(Catalogue.Subjects);
	}

	[Fact]
	public void dfe_transition_matrix_maps_average_gcse_band_to_real_probability()
	{
		var evidence = DfeTransitionMatrix.LoadDefault()
			.EvidenceFor(7.2, Harness.Catalogue)
			.Single(e => e.Subject == Subject.Maths);

		evidence.PriorAttainmentBand.Should().Be("7 to < 8");
		evidence.ProbabilityAtOrAbove(ALevelGrade.A)
			.Should().BeApproximately(0.5682336182336183, Tolerance);
	}

	[Fact]
	public void predicted_profile_carries_dfe_transition_evidence_for_every_subject()
	{
		var profile = Predict(Student(new() { ["maths"] = 7, ["art"] = 8 }));

		profile.TransitionEvidence.Select(e => e.Subject)
			.Should().BeEquivalentTo(Catalogue.Subjects);
		profile.TransitionEvidence.Should().OnlyContain(e => e.Source == DfeTransitionMatrix.Source);
	}

	[Fact]
	public void sparse_dfe_subject_band_falls_back_to_nearest_lower_populated_band()
	{
		var evidence = DfeTransitionMatrix.LoadDefault()
			.EvidenceFor(9.0, Harness.Catalogue)
			.Single(e => e.Subject == Subject.Art);

		evidence.PriorAttainmentBand.Should().Be("8 to < 9");
		evidence.ProbabilityAtOrAbove(ALevelGrade.B)
			.Should().BeApproximately(0.9842931937172774, Tolerance);
	}

	[Theory]
	[InlineData(9)] // all top grades — upper-clamp territory
	[InlineData(1)] // all bottom grades — lower-clamp territory
	[InlineData(4)] // a pass-boundary average
	public void predictions_stay_within_the_grade_range(int uniformGrade)
	{
		var gcses = new Dictionary<string, int> { ["maths"] = uniformGrade, ["physics"] = uniformGrade };

		var profile = Predict(Student(gcses));

		profile.PredictedGrades.Should().OnlyContain(p => p.PredictedPoints >= ALevelGrade.Min && p.PredictedPoints <= ALevelGrade.Max);
	}

	[Fact]
	public void all_nines_clamps_maths_to_the_top_of_the_scale()
	{
		// Maths at avg 9: 0.80*9 - 1.00 = 6.2, must clamp down to A* (6.0).
		var profile = Predict(Student(new() { ["maths"] = 9 }));

		profile.PredictedGrades.Single(p => p.Subject == Subject.Maths)
			.PredictedPoints.Should().Be(ALevelGrade.Max);
	}

	[Fact]
	public void all_ones_clamps_predictions_to_the_bottom_of_the_scale()
	{
		var profile = Predict(Student(new() { ["maths"] = 1 }));

		// Maths at avg 1: 0.80*1 - 1.00 = -0.2, must clamp up to U (0.0).
		profile.PredictedGrades.Single(p => p.Subject == Subject.Maths)
			.PredictedPoints.Should().Be(ALevelGrade.Min);
	}

	[Fact]
	public void single_gcse_does_not_throw_and_stays_in_range()
	{
		var act = () => Predict(Student(new() { ["maths"] = 5 }));

		act.Should().NotThrow();
		act().AverageGcseScore.Should().Be(5.0);
	}

	[Fact]
	public void no_gcses_yields_zero_average_without_throwing()
	{
		var profile = Predict(Student(new()));

		profile.AverageGcseScore.Should().Be(0.0);
		profile.PredictedGrades.Should().OnlyContain(p => p.PredictedPoints >= ALevelGrade.Min && p.PredictedPoints <= ALevelGrade.Max);
	}

	[Fact]
	public void hobbies_are_carried_through_unchanged()
	{
		var profile = Predict(Student(new() { ["maths"] = 6 }, "plays_piano", "chess_club"));

		profile.PredictedGrades.Should().NotBeEmpty();
		profile.Hobbies.Should().Equal("plays_piano", "chess_club");
	}

	[Fact]
	public void chosen_a_levels_are_carried_through_unchanged()
	{
		var student = Student(new() { ["maths"] = 6 });
		student = student with { ChosenALevels = [Subject.French, Subject.German] };

		var profile = Predict(student);

		profile.ChosenALevels.Should().Equal(Subject.French, Subject.German);
	}

	[Fact]
	public async Task cli_runs_on_the_example_fixture_and_emits_a_parseable_profile()
	{
		var examplePath = Path.Combine(Harness.RepoRoot, "examples", "student.json");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync([examplePath], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();

		var profile = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.StudentProfile);
		profile.Should().NotBeNull();
		profile!.Id.Should().Be("S-1001");
		profile.AverageGcseScore.Should().BeApproximately((9 + 7 + 7 + 7 + 6 + 6 + 5 + 6 + 8) / 9.0, Tolerance);
		profile.PredictedGrades.Should().HaveCount(Catalogue.Subjects.Count);
		profile.ChosenALevels.Should().Equal(Subject.History);
		profile.Hobbies.Should().Equal("plays_piano", "chess_club");
	}

	[Fact]
	public async Task cli_with_no_argument_is_a_usage_error()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		(await CliRunner.RunAsync([], stdout, stderr)).Should().Be(CliRunner.ExitUsage);
	}

	[Fact]
	public async Task cli_with_a_missing_file_is_an_input_error()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var missing = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json");

		(await CliRunner.RunAsync([missing], stdout, stderr)).Should().Be(CliRunner.ExitInput);
	}
}
