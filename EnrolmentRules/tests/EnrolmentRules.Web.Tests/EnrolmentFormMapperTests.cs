namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Domain;
using Models;
using Services;

public sealed class EnrolmentFormMapperTests
{
	private static readonly EnrolmentSession BaseSession = new(
		"student-1",
		new DateOnly(2009, 6, 1),
		[new("maths", 8), new(null, null), new("", null)],
		[new("Maths", QualificationType.ALevel, "a"), new(null, null, null)],
		["chess_club", "", "  ", "coding"],
		[new("maths"), new("physics")]);

	[Fact]
	public void ToStudentInput_ignores_empty_gcse_rows()
	{
		var student = EnrolmentFormMapper.ToStudentInput(BaseSession);

		student.Gcses!.Value.Should().HaveCount(1);
		student.Gcses.Value["maths"].Should().Be(8);
	}

	[Fact]
	public void ToStudentInput_maps_prior_qualifications_and_ignores_empty_rows()
	{
		var student = EnrolmentFormMapper.ToStudentInput(BaseSession);

		student.PriorQualifications.Should().Equal(new Qualification("Maths", QualificationType.ALevel, "a"));
	}

	[Fact]
	public void ToStudentInput_preserves_exact_non_empty_hobby_values()
	{
		var student = EnrolmentFormMapper.ToStudentInput(BaseSession);

		student.Hobbies.Should().Equal("chess_club", "coding");
	}

	[Fact]
	public void ToStudentInput_maps_date_of_birth()
	{
		var student = EnrolmentFormMapper.ToStudentInput(BaseSession);

		student.DateOfBirth.Should().Be(new(2009, 6, 1));
	}

	[Fact]
	public void ToStudentInput_maps_chosen_a_levels()
	{
		var student = EnrolmentFormMapper.ToStudentInput(BaseSession);

		student.ChosenALevels.Should().Equal(new Subject("maths"), new Subject("physics"));
	}

	[Fact]
	public void Apply_replaces_facts_but_preserves_student_id_and_chosen_a_levels()
	{
		var current = EnrolmentSession.Empty("student-2") with { ChosenALevels = [new("biology")] };
		var input = new SaveFactsInput(
			new DateOnly(2010, 1, 1),
			[new("physics", 7)],
			[new("English", QualificationType.ALevel, "b")],
			["swimming"]);

		var updated = EnrolmentFormMapper.Apply(input, current);

		updated.StudentId.Should().Be("student-2");
		updated.ChosenALevels.Should().Equal(new Subject("biology"));
		updated.DateOfBirth.Should().Be(new(2010, 1, 1));
		updated.Gcses.Should().Equal(new GcseRow("physics", 7));
		updated.PriorQualifications.Should().Equal(new PriorQualificationRow("English", QualificationType.ALevel, "b"));
		updated.Hobbies.Should().Equal("swimming");
	}
}
