namespace EnrolmentRules.Web.Tests;

using System.Globalization;
using System.Net;
using Api;
using AwesomeAssertions;

public sealed class EnrolmentEvaluateEndpointTests : IClassFixture<WebAppFactory>
{
	// examples/golden/strong-constraints.json — a known-eligible student with a stable mix of ratings
	// (see RenderExplanationsTests, which drives the same facts through the Razor form).
	private static readonly EvaluateGcseRow[] KnownGcses = [
		new("maths", 8), new("english_language", 8), new("english_literature", 8), new("physics", 8), new("chemistry", 8),
		new("biology", 8), new("french", 8), new("german", 8), new("physical_education", 8), new("computer_studies", 8),
		new("history", 8), new("music", 8), new("art", 8),
	];

	private readonly WebAppFactory factory;

	public EnrolmentEvaluateEndpointTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Valid_snapshot_returns_eligible_result_with_explanations()
	{
		using var client = factory.CreateClient();

		var response = await PostAsync(client, KnownRequest());

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await ReadBodyAsync(response);
		body.ValidationErrors.Should().BeEmpty();
		body.Result.Should().NotBeNull();
		body.Result!.Eligible.Should().BeTrue();
		body.Result.Explanations.Should().NotBeEmpty();
	}

	[Fact]
	public async Task Out_of_range_grade_returns_200_with_validation_errors()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with { Gcses = [new("maths", 15)] };

		var response = await PostAsync(client, request);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await ReadBodyAsync(response);
		body.ValidationErrors.Should().NotBeEmpty();
		body.Result.Should().BeNull();
	}

	[Fact]
	public async Task Unparseable_prior_qualification_type_returns_400()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with { PriorQualifications = [new("applied_science", "NotAQualificationType", "Merit")] };

		var response = await PostAsync(client, request);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task A_forged_red_subject_choice_is_rejected()
	{
		// A red subject is not a choice the student may hold, however the value got into the snapshot —
		// forged by hand, or left behind by GCSEs that were lowered after the choice was made. The engine
		// refuses the whole document rather than returning a verdict that honours it.
		using var client = factory.CreateClient();
		var request = KnownRequest() with { ChosenALevels = ["further_maths"] };

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		body.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle()
			.Which.Should().Contain("chosen_a_levels").And.Contain("further_maths");
	}

	[Fact]
	public async Task A_rejected_choice_is_named_so_the_client_can_eject_it()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with { ChosenALevels = ["further_maths"] };

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		body.EjectedChoices.Should().ContainSingle().Which.Value.Should().Be("further_maths");
	}

	[Fact]
	public async Task Lowering_gcses_ejects_a_choice_that_was_green_when_it_was_made()
	{
		using var client = factory.CreateClient();
		// French is green on the strong grades and accepted as a choice.
		var chosen = KnownRequest() with { ChosenALevels = ["french"] };
		var accepted = await ReadBodyAsync(await PostAsync(client, chosen));
		accepted.Result.Should().NotBeNull();
		accepted.EjectedChoices.Should().BeEmpty();

		// The same choice, with every grade collapsed to a 1.
		var lowered = chosen with { Gcses = [.. KnownGcses.Select(static row => row with { Grade = 1 })] };
		var body = await ReadBodyAsync(await PostAsync(client, lowered));

		body.Result.Should().BeNull();
		body.EjectedChoices.Should().ContainSingle().Which.Value.Should().Be("french");
	}

	[Fact]
	public async Task Re_posting_without_the_ejected_choice_evaluates_cleanly()
	{
		using var client = factory.CreateClient();
		var lowered = KnownRequest() with { Gcses = [.. KnownGcses.Select(static row => row with { Grade = 1 })], ChosenALevels = ["french"] };

		var rejected = await ReadBodyAsync(await PostAsync(client, lowered));
		var pruned = lowered with {
			ChosenALevels = [.. lowered.ChosenALevels.Where(subject => rejected.EjectedChoices.All(e => e.Value != subject))],
		};
		var body = await ReadBodyAsync(await PostAsync(client, pruned));

		// One prune is always enough — the re-post is accepted, so the client never loops.
		body.Result.Should().NotBeNull();
		body.EjectedChoices.Should().BeEmpty();
		body.ValidationErrors.Should().BeEmpty();
	}

	[Fact]
	public async Task Choosing_more_subjects_than_the_cap_reports_a_choice_limit_reason()
	{
		using var client = factory.CreateClient();
		var greenSubjects = new[] { "design_technology", "spanish", "law", "religious_studies", "politics", "geography", "psychology", "economics" };
		var request = KnownRequest() with { ChosenALevels = [.. greenSubjects] };

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		body.Result!.ChoiceLimitReason.Should().NotBeNull();
	}

	[Fact]
	public async Task Response_has_no_set_cookie_header()
	{
		using var client = factory.CreateClient();

		var response = await PostAsync(client, KnownRequest());

		response.Headers.Contains("Set-Cookie").Should().BeFalse();
	}

	[Fact]
	public async Task Identical_bodies_from_independent_clients_produce_equivalent_responses()
	{
		using var clientA = factory.CreateClient();
		using var clientB = factory.CreateClient();
		var request = KnownRequest();

		var responseA = await PostAsync(clientA, request);
		var responseB = await PostAsync(clientB, request);
		var bodyA = await ReadBodyAsync(responseA);
		var bodyB = await ReadBodyAsync(responseB);

		bodyA.Should().Be(bodyB);
	}

	[Fact]
	public async Task Matches_the_razor_workflow_for_the_same_facts()
	{
		using var razorClient = factory.CreateClient(new() { AllowAutoRedirect = false });
		using var getResponse = await razorClient.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);
		var form = new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
			["Hobbies[0]"] = "chess_club",
		};
		for (var i = 0; i < KnownGcses.Length; i++) {
			form[$"Gcses[{i}].Subject"] = KnownGcses[i].Subject!;
			form[$"Gcses[{i}].Grade"] = KnownGcses[i].Grade!.Value.ToString(CultureInfo.InvariantCulture);
		}

		using var content = new FormUrlEncodedContent(form);
		using var postResponse = await razorClient.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), content);
		using var followUp = await razorClient.GetAsync(postResponse.Headers.Location);
		var html = await followUp.Content.ReadAsStringAsync();

		using var apiClient = factory.CreateClient();
		var apiResponse = await PostAsync(apiClient, KnownRequest());
		var apiBody = await ReadBodyAsync(apiResponse);

		foreach (var subject in new[] { "physics", "art", "further_maths" }) {
			var explanation = apiBody.Result!.Explanations.Single(e => e.Subject.Value == subject);
			html.Should().Contain(subject).And.Contain(explanation.Rating);
		}
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

	private static EnrolmentEvaluateRequest KnownRequest() => new(
		new DateOnly(2009, 9, 1),
		[.. KnownGcses],
		[],
		["chess_club"],
		[]);

	private static async Task<HttpResponseMessage> PostAsync(HttpClient client, EnrolmentEvaluateRequest request) =>
		await client.PostAsJsonAsync("/api/enrolment/evaluate", request, EnrolmentApiJsonContext.Default.EnrolmentEvaluateRequest);

	private static async Task<EnrolmentEvaluateResponse> ReadBodyAsync(HttpResponseMessage response)
	{
		var body = await response.Content.ReadFromJsonAsync(EnrolmentApiJsonContext.Default.EnrolmentEvaluateResponse);
		body.Should().NotBeNull();
		return body!;
	}
}
