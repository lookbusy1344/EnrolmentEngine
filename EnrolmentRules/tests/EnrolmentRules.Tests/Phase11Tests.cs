namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;

/// <summary>
///     Phase 11 — typed prior qualifications as entry qualifiers. The predictor must lift a matching
///     prior qualification to its catalogue-defined equivalence, but never lower a stronger regression
///     prediction; the engine must then recognise the equivalent as an alternative entry path.
/// </summary>
public sealed class Phase11Tests
{
	private static StudentInput Student(
		Dictionary<string, int> gcses,
		params Qualification[] priorQualifications) =>
		new("S-ENTRY", gcses, []) { DateOfBirth = new DateOnly(2009, 9, 1), PriorQualifications = priorQualifications };

	private static QualificationScale BuildDiplomaScale(params (string Grade, int Ordinal, double Equivalence)[] grades) =>
		new(grades.Select(grade => new QualificationScaleEntry(QualificationType.BtecDiploma, grade.Grade, grade.Ordinal, grade.Equivalence)));

	[Fact]
	public void a_matching_entry_equivalent_raises_the_predicted_points_for_the_subject()
	{
		var student = Student(new() { ["english_language"] = 1, ["maths"] = 1, ["biology"] = 1 },
			new Qualification("applied_science", QualificationType.BtecDiploma, "distinction"));

		var profile = Harness.Predict(student);

		profile.PriorQualifications.Should().Equal(student.PriorQualifications);
		profile.PredictedGrades.Single(p => p.Subject == Subject.Biology).PredictedPoints.Should().Be(ALevelGrade.A);
	}

	[Fact]
	public void a_non_matching_prior_qualification_leaves_the_prediction_unchanged()
	{
		var student = Student(new() { ["english_language"] = 7, ["maths"] = 7, ["biology"] = 7 },
			new Qualification("applied_science", QualificationType.BtecDiploma, "pass"));

		var profile = Harness.Predict(student);
		var biology = profile.PredictedGrades.Single(p => p.Subject == Subject.Biology).PredictedPoints;

		biology.Should().BeApproximately(Catalogue.Meta(Subject.Biology).Regression.Predict(profile.AverageGcseScore), 1e-9);
	}

	[Fact]
	public void an_entry_equivalent_never_lowers_a_stronger_regression_prediction()
	{
		var student = Student(new() { ["english_language"] = 9, ["maths"] = 9, ["biology"] = 9 },
			new Qualification("applied_science", QualificationType.BtecDiploma, "distinction"));

		var profile = Harness.Predict(student);
		var biology = profile.PredictedGrades.Single(p => p.Subject == Subject.Biology).PredictedPoints;

		biology.Should().BeApproximately(Catalogue.Meta(Subject.Biology).Regression.Predict(profile.AverageGcseScore), 1e-9);
		biology.Should().BeGreaterThan(ALevelGrade.A);
	}

	[Fact]
	public async Task validation_honours_the_engines_injected_scale_not_the_ambient_default()
	{
		// 'platinum' exists only in this custom scale, never in the shipped/ambient default. Validating a
		// student carrying a platinum qualification must use the engine's own scale, not the shipped default.
		var customScale = BuildDiplomaScale(
			("pass", 0, ALevelGrade.C),
			("platinum", 1, ALevelGrade.AStar));
		var (_, rulesEngine) = await Harness.BuildFromShippedWorkflowsAsync();
		var engine = new EnrolmentEngine(
			new(rulesEngine, Harness.Thresholds, Harness.Catalogue, customScale), Harness.Catalogue, Harness.AsOf);

		var student = Student(new() { ["english_language"] = 7, ["maths"] = 7 },
			new Qualification("applied_science", QualificationType.BtecDiploma, "platinum"));

		StudentValidator.Validate(student, engine.Catalogue, engine.Scale).Should().BeEmpty();
		StudentValidator.Validate(student, engine.Catalogue, QualificationScale.Default)
			.Should().Contain(error => error.Contains("platinum"));
	}

	[Fact]
	public async Task an_entry_equivalent_opens_the_biology_entry_path_through_the_engine()
	{
		var engine = await Harness.ShippedEngineAsync();
		var withoutEquivalent = Student(new() {
			["english_language"] = 8,
			["maths"] = 8,
			["physics"] = 8,
			["chemistry"] = 8,
			["biology"] = 1,
			["english_literature"] = 8,
			["french"] = 8,
			["german"] = 8,
			["physical_education"] = 8,
			["computer_studies"] = 8,
			["history"] = 8,
			["music"] = 8,
			["art"] = 8,
		});
		var withEquivalent = withoutEquivalent with { PriorQualifications = [new("applied_science", QualificationType.BtecDiploma, "distinction")] };

		var without = await engine.EvaluateAsync(withoutEquivalent);
		var with = await engine.EvaluateAsync(withEquivalent);

		without.Recommendations.Single(r => r.Subject == Subject.Biology).Rating.Should().Be(Rating.Red);
		with.Recommendations.Single(r => r.Subject == Subject.Biology).Rating.Should().Be(Rating.Green);
	}

	[Fact]
	public async Task the_engine_keeps_its_injected_qualification_scale_after_construction()
	{
		var student = Student(new() {
			["english_language"] = 9,
			["maths"] = 9,
			["physics"] = 9,
			["chemistry"] = 1,
			["biology"] = 1,
			["english_literature"] = 9,
			["french"] = 9,
			["german"] = 9,
			["physical_education"] = 9,
			["computer_studies"] = 9,
			["history"] = 9,
			["music"] = 9,
			["art"] = 9,
		}, new Qualification("applied_science", QualificationType.BtecDiploma, "distinction_star"));

		var scaleA = BuildDiplomaScale(
			("pass", 0, ALevelGrade.C),
			("merit", 1, ALevelGrade.B),
			("distinction", 2, ALevelGrade.A),
			("distinction_star", 3, ALevelGrade.AStar));
		var scaleB = BuildDiplomaScale(
			("pass", 0, ALevelGrade.C),
			("merit", 1, ALevelGrade.B),
			("distinction_star", 2, ALevelGrade.AStar),
			("distinction", 3, ALevelGrade.A));

		var (_, rulesEngine) = await Harness.BuildFromShippedWorkflowsAsync();
		var catalogue = Harness.Catalogue;
		var thresholds = Harness.Thresholds;

		// Two engines over identical workflows differ only by the scale each was constructed with. The
		// distinction_star qualification clears Biology's distinction entry-equivalent under scaleA (where
		// distinction_star outranks distinction) but not under scaleB (where it ranks below) — proving each
		// engine evaluates against its own injected scale, with no ambient global to consult.
		var engineA = new EnrolmentEngine(new(rulesEngine, thresholds, catalogue, scaleA), catalogue, Harness.AsOf);
		var engineB = new EnrolmentEngine(new(rulesEngine, thresholds, catalogue, scaleB), catalogue, Harness.AsOf);

		var resultA = await engineA.EvaluateAsync(student);
		var resultB = await engineB.EvaluateAsync(student);

		resultA.Recommendations.Single(r => r.Subject == Subject.Biology).Rating.Should().Be(Rating.Green);
		resultB.Recommendations.Single(r => r.Subject == Subject.Biology).Rating.Should().Be(Rating.Red);
	}

	[Fact]
	public async Task the_engine_rejects_a_façade_catalogue_that_differs_from_the_evaluators_catalogue()
	{
		var (_, rulesEngine) = await Harness.BuildFromShippedWorkflowsAsync();
		var catalogue = Harness.Catalogue;
		var scale = BuildDiplomaScale(
			("pass", 0, ALevelGrade.C),
			("merit", 1, ALevelGrade.B),
			("distinction", 2, ALevelGrade.A),
			("distinction_star", 3, ALevelGrade.AStar));
		var mismatchedCatalogue = new CatalogueData(
			catalogue.Subjects.ToDictionary(
				static subject => subject,
				subject => catalogue.Meta(subject)),
			catalogue.Subjects);

		var evaluator = new RatingEvaluator(rulesEngine, Harness.Thresholds, catalogue, scale);
		var act = () => new EnrolmentEngine(evaluator, mismatchedCatalogue, Harness.AsOf);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*catalogue*");
	}
}
