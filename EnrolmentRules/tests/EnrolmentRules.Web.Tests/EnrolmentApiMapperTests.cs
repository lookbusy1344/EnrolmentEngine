namespace EnrolmentRules.Web.Tests;

using Api;
using AwesomeAssertions;
using Domain;

/// <summary>
///     The request → <see cref="StudentInput" /> boundary: blank rows dropped, non-blank rows carried
///     through at their boundary values (so <see cref="StudentValidator" /> reports them), and an
///     unparseable qualification type or chosen subject rejected outright (→ 400). Ported from the former
///     EnrolmentFormMapper tests when the intermediate session shape was removed.
/// </summary>
public sealed class EnrolmentApiMapperTests
{
	private static EnrolmentEvaluateRequest BaseRequest() => new(
		new DateOnly(2009, 6, 1),
		[new("maths", 8), new(null, null), new("", null)],
		[new("Maths", "ALevel", "a"), new(null, null, null)],
		["chess_club", "", "  ", "coding"],
		["maths", "physics"]);

	private static StudentInput Map(EnrolmentEvaluateRequest request)
	{
		EnrolmentApiMapper.TryToStudentInput(request, out var student).Should().BeTrue();
		return student!;
	}

	[Fact]
	public void ignores_empty_gcse_rows()
	{
		var student = Map(BaseRequest());

		student.Gcses!.Value.Should().HaveCount(1);
		student.Gcses.Value["maths"].Should().Be(8);
	}

	[Fact]
	public void maps_prior_qualifications_and_ignores_empty_rows()
	{
		var student = Map(BaseRequest());

		student.PriorQualifications.Should().Equal(new Qualification("Maths", QualificationType.ALevel, "a"));
	}

	[Fact]
	public void preserves_exact_non_empty_hobby_values()
	{
		var student = Map(BaseRequest());

		student.Hobbies.Should().Equal("chess_club", "coding");
	}

	[Fact]
	public void maps_date_of_birth()
	{
		var student = Map(BaseRequest());

		student.DateOfBirth.Should().Be(new(2009, 6, 1));
	}

	[Fact]
	public void maps_chosen_a_levels()
	{
		var student = Map(BaseRequest());

		student.ChosenALevels.Should().Equal(new Subject("maths"), new Subject("physics"));
	}

	[Fact]
	public void rejects_an_unparseable_qualification_type()
	{
		var request = BaseRequest() with {
			PriorQualifications = [new("Maths", "NotAType", "a")],
		};

		EnrolmentApiMapper.TryToStudentInput(request, out var student).Should().BeFalse();
		student.Should().BeNull();
	}

	[Fact]
	public void rejects_an_unparseable_chosen_a_level()
	{
		var request = BaseRequest() with {
			ChosenALevels = ["Not A Subject"],
		};

		EnrolmentApiMapper.TryToStudentInput(request, out var student).Should().BeFalse();
		student.Should().BeNull();
	}
}
