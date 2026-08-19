namespace EnrolmentRules.Web.Tests;

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using Api;
using AwesomeAssertions;
using Domain;

public sealed class EnrolmentEvaluateEndpointTests : IClassFixture<WebAppFactory>
{
	// examples/golden/strong-constraints.json — a known-eligible student with a stable mix of ratings
	// (see RenderExplanationsTests, which drives the same facts through the Razor form).
	private static readonly ImmutableArray<EvaluateGcseRow> KnownGcses = [
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
		var request = KnownRequest() with {
			Gcses = [new("maths", 15)],
		};

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
		var request = KnownRequest() with {
			PriorQualifications = [new("applied_science", "NotAQualificationType", "Merit")],
		};

		var response = await PostAsync(client, request);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task A_forged_red_subject_choice_stays_in_the_basket_marked_unavailable()
	{
		// Non-destructive: a chosen subject the engine now rates red — however it got into the snapshot —
		// is never ejected server-side. It stays in ChoiceStatuses as Unavailable, with the deciding reason,
		// so the client's basket never silently loses a selection.
		using var client = factory.CreateClient();
		var request = KnownRequest() with {
			ChosenALevels = ["further_maths"],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		body.ValidationErrors.Should().BeEmpty();
		body.Result.Should().NotBeNull();
		var status = body.Result!.ChoiceStatuses.Should().ContainSingle().Which;
		status.Subject.Value.Should().Be("further_maths");
		status.Status.Should().Be("Unavailable");
		status.Reason.Should().NotBeNull();
	}

	[Fact]
	public async Task Lowering_gcses_moves_a_choice_from_available_to_unavailable_without_dropping_it()
	{
		using var client = factory.CreateClient();
		// French is green on the strong grades and accepted as a choice.
		var chosen = KnownRequest() with {
			ChosenALevels = ["french"],
		};
		var accepted = await ReadBodyAsync(await PostAsync(client, chosen));
		accepted.Result.Should().NotBeNull();
		accepted.Result!.ChoiceStatuses.Should().ContainSingle(s => s.Subject.Value == "french" && s.Status == "Available");

		// The same choice, with every grade collapsed to a 1.
		var lowered = chosen with {
			Gcses = [
				.. KnownGcses.Select(static row => row with {
					Grade = 1,
				}),
			],
		};
		var body = await ReadBodyAsync(await PostAsync(client, lowered));

		body.Result.Should().NotBeNull();
		body.Result!.ChoiceStatuses.Should().ContainSingle(s => s.Subject.Value == "french" && s.Status == "Unavailable");
	}

	[Fact]
	public async Task An_unavailable_choice_survives_unchanged_across_a_second_identical_request()
	{
		using var client = factory.CreateClient();
		var lowered = KnownRequest() with {
			Gcses = [
				.. KnownGcses.Select(static row => row with {
					Grade = 1,
				}),
			],
			ChosenALevels = ["french"],
		};

		var first = await ReadBodyAsync(await PostAsync(client, lowered));
		var second = await ReadBodyAsync(await PostAsync(client, lowered));

		first.Result!.ChoiceStatuses.Should().BeEquivalentTo(second.Result!.ChoiceStatuses);
		second.Result.ChoiceStatuses.Should().ContainSingle(s => s.Subject.Value == "french" && s.Status == "Unavailable");
	}

	[Fact]
	public async Task A_chosen_subject_outside_the_selected_policys_catalogue_is_not_offered()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with {
			ChosenALevels = ["not_a_real_subject_key"],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		body.ValidationErrors.Should().BeEmpty();
		body.Result.Should().NotBeNull();
		body.Result!.ChoiceStatuses.Should().ContainSingle(s => s.Subject.Value == "not_a_real_subject_key" && s.Status == "NotOffered");
	}

	[Fact]
	public async Task Choosing_more_subjects_than_the_cap_reports_a_choice_limit_reason()
	{
		using var client = factory.CreateClient();
		var greenSubjects = new[] {
			"design_technology", "spanish", "law", "religious_studies", "sociology", "media_studies", "psychology", "economics",
		};
		var request = KnownRequest() with {
			ChosenALevels = [.. greenSubjects],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		body.Result!.ChoiceLimitReason.Should().NotBeNull();
	}

	[Fact]
	public async Task Unavailable_and_not_offered_choices_do_not_count_towards_the_choice_limit()
	{
		using var client = factory.CreateClient();
		// Three placed choices, plus a red-but-offered choice (Politics rates red for this student) and
		// a not-offered key. Only the three that hold a place count against the four-subject cap, so no
		// limit is reported — the unavailable/not-offered pair must not fill the basket.
		var request = KnownRequest() with {
			ChosenALevels = ["psychology", "sociology", "media_studies", "politics", "not_a_real_subject_key"],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		body.Result.Should().NotBeNull();
		body.Result!.ChoiceStatuses.Should().Contain(s => s.Subject.Value == "politics" && s.Status == "Unavailable");
		body.Result.ChoiceStatuses.Should().Contain(s => s.Subject.Value == "not_a_real_subject_key" && s.Status == "NotOffered");
		body.Result.ChoiceLimitReason.Should().BeNull();
	}

	[Fact]
	public async Task Too_many_gcse_rows_returns_200_with_a_validation_error()
	{
		using var client = factory.CreateClient();
		// Duplicate subject keys, so the mapped StudentInput would collapse to one GCSE — the row count must be
		// bounded on the raw posted array, before mapping, or this would slip past the boundary check entirely.
		var tooMany = Enumerable.Range(0, GcseSubjects.Known.Count + 1).Select(static _ => new EvaluateGcseRow("maths", 6)).ToArray();
		var request = KnownRequest() with {
			Gcses = [.. tooMany],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		body.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle().Which.Should().Contain("gcses");
	}

	[Fact]
	public async Task Too_many_chosen_a_levels_returns_200_with_a_validation_error()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with {
			ChosenALevels = [.. Enumerable.Range(0, 200).Select(static i => $"subject_{i}")],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		body.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle().Which.Should().Contain("chosen_a_levels");
	}

	[Fact]
	public async Task Too_many_prior_qualifications_returns_200_with_a_validation_error()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with {
			PriorQualifications = [
				.. Enumerable.Range(0, 51).Select(static i => new EvaluatePriorQualificationRow($"subject_{i}", "BtecDiploma", "distinction")),
			],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		body.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle().Which.Should().Contain("prior_qualifications");
	}

	[Fact]
	public async Task Too_many_hobbies_returns_200_with_a_validation_error()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with {
			Hobbies = [.. Enumerable.Range(0, 51).Select(static i => $"hobby_{i}")],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		body.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle().Which.Should().Contain("hobbies");
	}

	[Fact]
	public async Task An_excessively_long_hobby_tag_returns_200_with_a_validation_error()
	{
		using var client = factory.CreateClient();
		var request = KnownRequest() with {
			Hobbies = [new('x', 101)],
		};

		var response = await PostAsync(client, request);
		var body = await ReadBodyAsync(response);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		body.Result.Should().BeNull();
		body.ValidationErrors.Should().ContainSingle().Which.Should().Contain("hobbies[0]").And.Contain("length");
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
		using var razorClient = factory.CreateClient(new() {
			AllowAutoRedirect = false,
		});
		using var getResponse = await razorClient.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);
		var form = new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
			["Hobbies[0]"] = "chess_club",
		};
		for (var i = 0; i < KnownGcses.Length; ++i) {
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

		foreach (var subject in new[] {
					 "physics", "art", "further_maths",
				 }) {
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
