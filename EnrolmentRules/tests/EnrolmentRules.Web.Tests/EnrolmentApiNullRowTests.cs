namespace EnrolmentRules.Web.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using Api;
using AwesomeAssertions;

public sealed class EnrolmentApiNullRowTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public EnrolmentApiNullRowTests(WebAppFactory factory) => this.factory = factory;

	[Theory]
	[InlineData("gcses")]
	[InlineData("priorQualifications")]
	public async Task Null_rows_return_structured_validation_errors(string field)
	{
		using var client = factory.CreateClient();
		using var content = new StringContent(NullRowJson(field), Encoding.UTF8, "application/json");

		using var response = await client.PostAsync(new Uri("/api/enrolment/evaluate", UriKind.Relative), content);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync(EnrolmentApiJsonContext.Default.EnrolmentEvaluateResponse);
		body.Should().NotBeNull();
		body!.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle().Which.Should().Contain("[0]").And.Contain("required");
	}

	[Theory]
	[InlineData("gcses")]
	[InlineData("priorQualifications")]
	public void Mapper_rejects_null_rows_without_throwing(string field)
	{
		var request = JsonSerializer.Deserialize(NullRowJson(field), EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest)!;

		EnrolmentApiMapper.TryToStudentInput(request, out var student).Should().BeFalse();
		student.Should().BeNull();
	}

	private static string NullRowJson(string field) => $$"""{"{{field}}":[null]}""";
}
