namespace EnrolmentRules.Web.Tests;

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Api;
using AwesomeAssertions;

/// <summary>
///     Elite auxiliary policy plan, step 3.4-3.5 — <c>?policy=</c> selection on the Web API and the Razor
///     page. The Vue client's own selection/persistence logic is covered separately (step 3.6).
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

	// --- Razor ---

	[Fact]
	public async Task Default_load_selects_standard()
	{
		using var client = factory.CreateClient();

		var html = await client.GetStringAsync(new Uri("/razor", UriKind.Relative));

		html.Should().Contain("Standard");
	}

	[Fact]
	public async Task Url_selection_loads_elite_and_marks_it_current()
	{
		using var client = factory.CreateClient();

		var html = await client.GetStringAsync(new Uri("/razor?policy=elite", UriKind.Relative));

		html.Should().Contain("Elite");
		html.Should().Contain("Switch to Standard");
	}

	[Fact]
	public async Task An_invalid_policy_query_value_redirects_to_the_canonical_url_rather_than_silently_falling_back()
	{
		using var client = factory.CreateClient(new() {
			AllowAutoRedirect = false,
		});

		using var response = await client.GetAsync(new Uri("/razor?policy=nonexistent", UriKind.Relative));

		((int)response.StatusCode).Should().BeInRange(300, 399);
	}

	[Fact]
	public async Task Switching_policy_via_the_top_link_preserves_facts_and_the_exact_basket()
	{
		using var client = factory.CreateClient(new() {
			AllowAutoRedirect = false,
		});

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);
		var form = new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
		};
		for (var i = 0; i < EliteEligibleGcses.Length; ++i) {
			form[$"Gcses[{i}].Subject"] = EliteEligibleGcses[i].Subject!;
			form[$"Gcses[{i}].Grade"] = EliteEligibleGcses[i].Grade!.Value.ToString(CultureInfo.InvariantCulture);
		}

		using var saveContent = new FormUrlEncodedContent(form);
		using var saveResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), saveContent);
		using var afterSave = await client.GetAsync(saveResponse.Headers.Location);

		var chooseToken = await ExtractAntiForgeryTokenAsync(afterSave);
		using var chooseContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = chooseToken,
			["subject"] = "biology",
		});
		using var chooseResponse = await client.PostAsync(new Uri("/razor?handler=ChooseSubject", UriKind.Relative), chooseContent);
		using var afterChoose = await client.GetAsync(chooseResponse.Headers.Location);
		var htmlAfterChoose = await afterChoose.Content.ReadAsStringAsync();
		htmlAfterChoose.Should().Contain("Biology");

		// Follow the top switch link to Elite: same session, same basket.
		using var eliteResponse = await client.GetAsync(new Uri("/razor?policy=elite", UriKind.Relative));
		var htmlUnderElite = await eliteResponse.Content.ReadAsStringAsync();

		htmlUnderElite.Should().Contain("Biology");
		htmlUnderElite.Should().Contain("value=\"2009-09-01\""); // date of birth preserved across the switch
	}

	private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
	{
		var html = await response.Content.ReadAsStringAsync();
		const string marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
		var start = html.IndexOf(marker, StringComparison.Ordinal);
		start.Should().BeGreaterThan(-1, "the page must render the anti-forgery token");
		var valueStart = start + marker.Length;
		var valueEnd = html.IndexOf('"', valueStart);
		return html[valueStart..valueEnd];
	}
}
