namespace EnrolmentRules.Tests;

using System.Text.Json;
using Domain;
using FluentAssertions;

/// <summary>
///     Phase 11 — typed prior qualifications and the qualification scale. Pins the new JSON contract,
///     the data-backed lookup surface, and the input validator's handling of malformed prior
///     qualifications before the feature is wired into prediction and the engine.
/// </summary>
public sealed class QualificationScaleTests
{
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
			.Which.Should().Contain("prior_qualifications").And.Contain("biology");
	}
}
