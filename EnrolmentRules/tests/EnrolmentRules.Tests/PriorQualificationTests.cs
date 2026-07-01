namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;
using Engine;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

/// <summary>
///     Typed prior qualifications — entry qualifiers, qualification scale resolution, and the restudy bar.
///     Merged from the former Phase11Tests, QualificationScaleTests, and RestudyBarTests.
/// </summary>
public sealed class PriorQualificationTests
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
				catalogue.Meta),
			catalogue.Subjects);

		var evaluator = new RatingEvaluator(rulesEngine, Harness.Thresholds, catalogue, scale);
		var act = () => new EnrolmentEngine(evaluator, mismatchedCatalogue, Harness.AsOf);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*catalogue*");
	}
}

/// <summary>
///     Qualification scale — typed prior qualification types, serialization, ordinal resolution,
///     entry equivalence, and the scale load/validate guard. Pins the JSON contract and the
///     startup invariant checks independently of the prediction and engine wiring.
/// </summary>
public sealed class QualificationScaleResolutionTests
{
	private static string ScaleSchema => File.ReadAllText(Path.Combine(Harness.DataDir, QualificationScaleStore.SchemaFileName));

	[Theory]
	[InlineData(QualificationType.Gcse, "\"gcse\"")]
	[InlineData(QualificationType.ALevel, "\"a_level\"")]
	[InlineData(QualificationType.BtecExtendedCertificate, "\"btec_extended_certificate\"")]
	public void qualification_type_serialises_to_snake_case(QualificationType type, string expected) =>
		JsonSerializer.Serialize(type).Should().Be(expected);

	[Fact]
	public void student_document_round_trips_prior_qualifications()
	{
		var document = new StudentDocument(new(
			"S-QUAL",
			new Dictionary<string, int> { ["maths"] = 6 },
			[]) {
			PriorQualifications = [
				new("biology", QualificationType.BtecDiploma, "distinction"),
			],
		});

		var json = JsonSerializer.Serialize(document, EnrolmentJsonContext.Default.StudentDocument);

		json.Should().Contain("\"prior_qualifications\"");
		json.Should().Contain("\"btec_diploma\"");

		var roundTripped = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentDocument);
		roundTripped.Should().NotBeNull();
		roundTripped!.Student.PriorQualifications.Should().ContainSingle().Which.Should()
			.Be(new Qualification("biology", QualificationType.BtecDiploma, "distinction"));
	}

	[Fact]
	public void student_document_deserialisation_allows_a_prior_qualification_grade_to_be_missing()
	{
		const string json = """
							{
							  "student": {
							    "id": "S-QUAL",
							    "gcses": { "maths": 6 },
							    "hobbies": [],
							    "prior_qualifications": [
							      { "subject": "biology", "type": "a_level" }
							    ]
							  }
							}
							""";

		var document = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentDocument);

		document.Should().NotBeNull();
		document!.Student.PriorQualifications.Should().ContainSingle();
		document.Student.PriorQualifications[0].Grade.Should().BeNull();
	}

	[Fact]
	public void qualification_scale_resolves_ordinal_and_equivalence()
	{
		var scale = new QualificationScale([
			new(QualificationType.ALevel, "u", 0, ALevelGrade.U),
			new(QualificationType.ALevel, "e", 1, ALevelGrade.E),
			new(QualificationType.ALevel, "d", 2, ALevelGrade.D),
			new(QualificationType.ALevel, "c", 3, ALevelGrade.C),
			new(QualificationType.ALevel, "b", 4, ALevelGrade.B),
			new(QualificationType.ALevel, "a", 5, ALevelGrade.A),
			new(QualificationType.ALevel, "a_star", 6, ALevelGrade.AStar),
			new(QualificationType.BtecDiploma, "pass", 0, ALevelGrade.C),
			new(QualificationType.BtecDiploma, "merit", 1, ALevelGrade.B),
			new(QualificationType.BtecDiploma, "distinction", 2, ALevelGrade.A),
			new(QualificationType.BtecDiploma, "distinction_star", 3, ALevelGrade.AStar),
		]);

		scale.Ordinal(QualificationType.ALevel, "a").Should().Be(5);
		scale.Equivalence(QualificationType.BtecDiploma, "distinction").Should().Be(ALevelGrade.A);
	}

	[Fact]
	public void qualification_scale_throws_for_an_unknown_type_grade_pair()
	{
		var scale = new QualificationScale([
			new(QualificationType.ALevel, "u", 0, ALevelGrade.U),
		]);

		var act = () => scale.Ordinal(QualificationType.ALevel, "x");

		act.Should().Throw<InvalidDataException>().WithMessage("*unknown qualification*");
	}

	[Fact]
	public void qualification_scale_try_ordinal_returns_false_for_an_unknown_type_grade_pair()
	{
		var scale = new QualificationScale([
			new(QualificationType.ALevel, "u", 0, ALevelGrade.U),
		]);

		scale.TryOrdinal(QualificationType.ALevel, "x", out var ordinal).Should().BeFalse();
		ordinal.Should().Be(0);
	}

	[Fact]
	public void qualification_scale_treats_a_null_grade_as_unknown_in_both_try_and_hard_lookups()
	{
		var scale = new QualificationScale([
			new(QualificationType.ALevel, "u", 0, ALevelGrade.U),
		]);

		scale.TryOrdinal(QualificationType.ALevel, null, out var ordinal).Should().BeFalse();
		ordinal.Should().Be(0);

		var ordinalAct = () => scale.Ordinal(QualificationType.ALevel, null);
		var equivalenceAct = () => scale.Equivalence(QualificationType.ALevel, null);

		ordinalAct.Should().Throw<InvalidDataException>().WithMessage("*unknown qualification*a_level grade*");
		equivalenceAct.Should().Throw<InvalidDataException>().WithMessage("*unknown qualification*a_level grade*");
	}

	[Fact]
	public void student_validator_rejects_an_unresolvable_prior_qualification_grade()
	{
		var student = new StudentInput(
			"S-BAD",
			new Dictionary<string, int> { ["maths"] = 6 },
			[]) { DateOfBirth = new DateOnly(2009, 9, 1), PriorQualifications = [new("biology", QualificationType.ALevel, "not-a-grade")] };

		var scale = new QualificationScale([
			new(QualificationType.ALevel, "u", 0, ALevelGrade.U),
		]);

		StudentValidator.Validate(student, Harness.Catalogue, scale)
			.Should()
			.ContainSingle()
			.Which.Should().Contain("prior_qualifications").And.Contain("biology").And.Contain("not-a-grade");
	}

	[Fact]
	public void student_validator_returns_a_message_for_a_null_prior_qualification_grade()
	{
		var student = new StudentInput(
			"S-BAD",
			new Dictionary<string, int> { ["maths"] = 6 },
			[]) { DateOfBirth = new DateOnly(2009, 9, 1), PriorQualifications = [new("biology", QualificationType.ALevel, null!)] };

		var act = () => StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale);

		act.Should().NotThrow();
		act().Should().ContainSingle().Which.Should()
			.Contain("prior_qualifications")
			.And.Contain("biology")
			.And.Contain("unknown qualification")
			.And.Contain("''");
	}

	[Property(Arbitrary = new[] { typeof(MalformedStudentArbitraries) }, MaxTest = 200)]
	public bool student_validator_validate_is_total_for_malformed_student_documents(StudentInput student)
	{
		var act = () => StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale);

		act.Should().NotThrow();
		return true;
	}

	[Fact]
	public void load_and_validate_rejects_a_scale_missing_a_known_qualification_type()
	{
		const string missingNvq = """
								  qualifications:
								    - type: gcse
								      grades:
								        - { grade: "1", ordinal: 1, equivalence: 0.0 }
								    - type: a_level
								      grades:
								        - { grade: u, ordinal: 0, equivalence: 0.0 }
								    - type: btec_extended_certificate
								      grades:
								        - { grade: pass, ordinal: 0, equivalence: 2.0 }
								    - type: btec_diploma
								      grades:
								        - { grade: pass, ordinal: 0, equivalence: 3.0 }
								  """;

		var act = () => QualificationScaleStore.LoadAndValidate(
			new StringReader(missingNvq),
			new StringReader(ScaleSchema),
			"missing-nvq.yaml");

		act.Should().Throw<QualificationScaleException>()
			.WithMessage("*missing-nvq.yaml*")
			.Which.Message.Should().Contain("nvq");
	}

	[Fact]
	public void load_and_validate_rejects_duplicate_type_entries_with_a_startup_exception()
	{
		const string duplicateType = """
									 qualifications:
									   - type: gcse
									     grades:
									       - { grade: "1", ordinal: 1, equivalence: 0.0 }
									   - type: gcse
									     grades:
									       - { grade: "1", ordinal: 1, equivalence: 0.0 }
									   - type: a_level
									     grades:
									       - { grade: u, ordinal: 0, equivalence: 0.0 }
									   - type: btec_extended_certificate
									     grades:
									       - { grade: pass, ordinal: 0, equivalence: 2.0 }
									   - type: btec_diploma
									     grades:
									       - { grade: pass, ordinal: 0, equivalence: 3.0 }
									   - type: nvq
									     grades:
									       - { grade: level_1, ordinal: 1, equivalence: 1.0 }
									 """;

		var act = () => QualificationScaleStore.LoadAndValidate(
			new StringReader(duplicateType),
			new StringReader(ScaleSchema),
			"duplicate-type.yaml");

		act.Should().Throw<QualificationScaleException>()
			.WithMessage("*duplicate-type.yaml*")
			.Which.Message.Should().Contain("duplicate entry for gcse grade '1'");
	}

	[Fact]
	public void constructor_allows_deliberately_partial_in_memory_scales()
	{
		var scale = new QualificationScale([
			new(QualificationType.ALevel, "u", 0, ALevelGrade.U),
			new(QualificationType.ALevel, "e", 1, ALevelGrade.E),
		]);

		scale.Ordinal(QualificationType.ALevel, "e").Should().Be(1);
	}
}

/// <summary>
///     The restudy bar — the downgrade half of the prior-qualification feature. Demotes a subject when
///     the student already holds a barred qualification in the same subject, but leaves the subject
///     unchanged for unrelated prior qualifications.
/// </summary>
public sealed class RestudyBarConstraintTests
{
	private static StudentInput StrongStudent(params Qualification[] priorQualifications) =>
		new(
			"S-RB",
			new Dictionary<string, int> {
				["english_language"] = 8,
				["maths"] = 8,
				["physics"] = 8,
				["chemistry"] = 8,
				["biology"] = 8,
				["english_literature"] = 8,
				["french"] = 8,
				["german"] = 8,
				["physical_education"] = 8,
				["computer_studies"] = 8,
				["history"] = 8,
				["music"] = 8,
				["art"] = 8,
			},
			[]) { DateOfBirth = new DateOnly(2009, 9, 1), PriorQualifications = priorQualifications };

	[Fact]
	public void a_same_subject_prior_a_level_triggers_the_restudy_bar()
	{
		var profile = Harness.Predict(
			StrongStudent(new Qualification(Subject.Biology.Value, QualificationType.ALevel, "e")));

		var adjustments = ConstraintPass.Evaluate([
			new(Subject.Biology, Rating.Green, "base"),
		], profile, Harness.Catalogue);

		adjustments.Should().ContainSingle().Which.Should().Match<Adjustment>(adjustment =>
			adjustment.Subject == Subject.Biology
			&& adjustment.To == Rating.Red
			&& adjustment.Reason.Contains("already holds", StringComparison.Ordinal));
	}

	[Fact]
	public void a_prior_qualification_in_a_different_subject_does_not_trigger_the_bar()
	{
		var profile = Harness.Predict(
			StrongStudent(new Qualification(Subject.Physics.Value, QualificationType.ALevel, "e")));

		ConstraintPass.Evaluate([
				new(Subject.Biology, Rating.Green, "base"),
			], profile, Harness.Catalogue)
			.Should()
			.BeEmpty();
	}

	[Fact]
	public async Task the_restudy_bar_overrides_biology_through_the_engine()
	{
		var engine = await Harness.ShippedEngineAsync();
		var result = await engine.EvaluateAsync(StrongStudent(
			new Qualification(Subject.Biology.Value, QualificationType.ALevel, "e")));

		var biology = result.Recommendations.Single(r => r.Subject == Subject.Biology);
		biology.Rating.Should().Be(Rating.Red);
		biology.Reason.Should().Contain("already holds");
	}
}

public static class MalformedStudentArbitraries
{
	private static readonly Gen<string?> OptionalText =
		Gen.Elements(null, string.Empty, " ", "biology", "S-MALFORMED", "not-a-grade");

	private static readonly Gen<Qualification> Qualification =
		from subject in OptionalText
		from type in Gen.Elements([.. Enum.GetValues<QualificationType>()])
		from grade in OptionalText
		select new Qualification(subject!, type, grade!);

	private static readonly Gen<string> Hobby =
		Gen.Elements<string>(null!, string.Empty, " ", "plays_piano", "gaming");

	public static Arbitrary<StudentInput> StudentInput() =>
		Arb.From(
			from id in OptionalText
			from includeGcses in Gen.Elements(true, false)
			from includeHobbies in Gen.Elements(true, false)
			from hobbies in includeHobbies
				? Hobby.ListOf().Select(static items => (EquatableArray<string>?)items.ToArray())
				: Gen.Constant<EquatableArray<string>?>(null)
			from priorQualifications in Qualification.ListOf()
			from includeBirthDate in Gen.Elements(true, false)
			select new StudentInput(
				id!,
				includeGcses
					? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["maths"] = 6 }
					: null,
				hobbies) { DateOfBirth = includeBirthDate ? new DateOnly(2009, 9, 1) : null, PriorQualifications = [.. priorQualifications] });
}
