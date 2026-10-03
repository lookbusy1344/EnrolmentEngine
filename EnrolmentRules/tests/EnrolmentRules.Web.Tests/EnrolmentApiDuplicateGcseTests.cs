namespace EnrolmentRules.Web.Tests;

using System.Net;
using Api;
using AwesomeAssertions;
using Domain;

public sealed class EnrolmentApiDuplicateGcseTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public EnrolmentApiDuplicateGcseTests(WebAppFactory factory) => this.factory = factory;

	[Theory]
	[InlineData(Thresholds.MinGcseGrade, Thresholds.MaxGcseGrade)]
	[InlineData(Thresholds.MaxGcseGrade, Thresholds.MinGcseGrade)]
	[InlineData(Thresholds.MaxGcseGrade, Thresholds.MaxGcseGrade)]
	public async Task Duplicate_gcse_subjects_return_validation_errors_regardless_of_grades(int firstGrade, int secondGrade)
	{
		using var client = factory.CreateClient();
		var request = Request([new("maths", firstGrade), new("maths", secondGrade)]);

		using var response = await client.PostAsJsonAsync("/api/enrolment/evaluate", request, EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync(EnrolmentApiJsonContext.Default.EnrolmentEvaluateResponse);
		body.Should().NotBeNull();
		body!.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle().Which.Should().Contain("gcses[1]").And.Contain("duplicates 'maths'");
	}

	[Theory]
	[InlineData(Thresholds.MinGcseGrade, Thresholds.MaxGcseGrade)]
	[InlineData(Thresholds.MaxGcseGrade, Thresholds.MinGcseGrade)]
	[InlineData(Thresholds.MaxGcseGrade, Thresholds.MaxGcseGrade)]
	public void Mapper_rejects_duplicate_gcse_subjects_without_returning_partial_facts(int firstGrade, int secondGrade)
	{
		var request = Request([new("maths", firstGrade), new("maths", secondGrade)]);

		EnrolmentApiMapper.TryToStudentInput(request, out var student).Should().BeFalse();
		student.Should().BeNull();
	}

	[Fact]
	public async Task Repeated_empty_rows_are_ignored_and_distinct_subjects_keep_their_grades()
	{
		using var client = factory.CreateClient();
		var request = Request([
			new(null, null), new("", null), new("", null), new("  ", null),
			new("maths", Thresholds.MaxGcseGrade), new("english_language", Thresholds.MinGcseGrade),
		]);

		EnrolmentApiMapper.TryToStudentInput(request, out var student).Should().BeTrue();
		student!.Gcses!.Value.Should().HaveCount(2);
		student.Gcses.Value["maths"].Should().Be(Thresholds.MaxGcseGrade);
		student.Gcses.Value["english_language"].Should().Be(Thresholds.MinGcseGrade);
		using var response = await client.PostAsJsonAsync("/api/enrolment/evaluate", request, EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync(EnrolmentApiJsonContext.Default.EnrolmentEvaluateResponse);
		body!.ValidationErrors.Should().BeEmpty();
		body.Result.Should().NotBeNull();
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("", "  ")]
	[InlineData("  ", null)]
	public async Task Graded_rows_without_a_subject_return_indexed_validation_errors(string? firstSubject, string? secondSubject)
	{
		using var client = factory.CreateClient();
		var request = Request([
			new("maths", Thresholds.MaxGcseGrade),
			new(firstSubject, Thresholds.MinGcseGrade),
			new(secondSubject, Thresholds.MaxGcseGrade),
		]);

		using var response = await client.PostAsJsonAsync("/api/enrolment/evaluate", request, EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync(EnrolmentApiJsonContext.Default.EnrolmentEvaluateResponse);
		body.Should().NotBeNull();
		body!.Result.Should().BeNull();
		body.ValidationErrors.Should().Equal("gcses[1].subject is required", "gcses[2].subject is required");
	}

	[Fact]
	public async Task A_single_graded_row_without_a_subject_returns_an_indexed_validation_error()
	{
		using var client = factory.CreateClient();
		var request = Request([new("maths", Thresholds.MaxGcseGrade), new(null, Thresholds.MinGcseGrade)]);

		using var response = await client.PostAsJsonAsync("/api/enrolment/evaluate", request, EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync(EnrolmentApiJsonContext.Default.EnrolmentEvaluateResponse);
		body!.Result.Should().BeNull();
		body.ValidationErrors.Should().Equal("gcses[1].subject is required");
	}

	private static EnrolmentEvaluateRequest Request(EquatableArray<EvaluateGcseRow?> gcses) =>
		new(new DateOnly(2009, 9, 1), gcses, [], [], []);
}
