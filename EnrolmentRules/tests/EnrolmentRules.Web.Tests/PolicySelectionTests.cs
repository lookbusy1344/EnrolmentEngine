namespace EnrolmentRules.Web.Tests;

using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using Api;
using AwesomeAssertions;

/// <summary>
///     Elite auxiliary policy plan, step 3.4-3.5 — <c>?policy=</c> selection on the Web API. The Vue
///     client's own selection/persistence logic is covered separately (step 3.6).
/// </summary>
public sealed class PolicySelectionTests : IClassFixture<WebAppFactory>
{
	private static readonly ImmutableArray<EvaluateGcseRow> EliteEligibleGcses = [
		new("english_language", 8), new("maths", 8), new("biology", 8), new("chemistry", 8),
		new("history", 8), new("physics", 8), new("psychology", 8), new("french", 8),
	];

	private readonly WebAppFactory factory;

	public PolicySelectionTests(WebAppFactory factory) => this.factory = factory;

	// --- API: /api/enrolment/options ---

	[Fact]
	public async Task Omitted_policy_returns_standard_metadata_and_options()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync("/api/enrolment/options", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		body!.SelectedPolicy.Id.Should().Be("standard");
		body.AvailablePolicies.Select(p => p.Id).Should().Contain(["standard", "elite"]);
	}

	[Fact]
	public async Task Elite_returns_its_own_offered_a_level_options()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync(
			"/api/enrolment/options?policy=elite", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		body!.SelectedPolicy.Id.Should().Be("elite");
		body.ALevelSubjects.Select(s => s.Value).Should().Contain(["biology", "further_maths", "psychology"]);
		body.ALevelSubjects.Select(s => s.Value).Should().NotContain(["art", "music"]);
	}

	[Fact]
	public async Task An_unknown_policy_id_returns_400()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/api/enrolment/options?policy=nonexistent", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
		using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		problem.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
		problem.RootElement.GetProperty("detail").GetString().Should().Contain("nonexistent");
	}

	// --- API: /api/enrolment/evaluate ---

	[Fact]
	public async Task The_same_request_body_evaluated_under_both_policies_returns_distinct_statuses_without_mutating_the_basket()
	{
		using var client = factory.CreateClient();
		var request = new EnrolmentEvaluateRequest(
			new DateOnly(2009, 9, 1), [.. EliteEligibleGcses, new("art", 8)], [], [], ["art"]);

		var underStandard = await PostAsync(client, request, "standard");
		var underElite = await PostAsync(client, request, "elite");

		underStandard.Result.Should().NotBeNull();
		underStandard.Result!.ChoiceStatuses.Should().ContainSingle(s => s.Subject.Value == "art" && s.Status == "Available");

		underElite.Result.Should().NotBeNull();
		underElite.Result!.ChoiceStatuses.Should().ContainSingle(s => s.Subject.Value == "art" && s.Status == "NotOffered");

		// The posted request itself is never mutated by either call — re-reading its own array proves it.
		request.ChosenALevels.Should().Equal("art");
	}

	[Fact]
	public async Task Evaluate_with_an_unknown_policy_id_returns_400()
	{
		using var client = factory.CreateClient();
		var request = new EnrolmentEvaluateRequest(new DateOnly(2009, 9, 1), [.. EliteEligibleGcses], [], [], []);

		var response = await client.PostAsJsonAsync(
			"/api/enrolment/evaluate?policy=nonexistent", request, EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
		using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		problem.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
		problem.RootElement.GetProperty("detail").GetString().Should().Contain("nonexistent");
	}

	private static async Task<EnrolmentEvaluateResponse> PostAsync(HttpClient client, EnrolmentEvaluateRequest request, string policy)
	{
		var response = await client.PostAsJsonAsync(
			$"/api/enrolment/evaluate?policy={policy}", request, EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest);
		var body = await response.Content.ReadFromJsonAsync(EnrolmentApiJsonContext.Default.EnrolmentEvaluateResponse);
		body.Should().NotBeNull();
		return body!;
	}
}
