namespace EnrolmentRules.Web.Tests;

using System.Globalization;
using System.Net;
using AwesomeAssertions;

/// <summary>
///     <c>?handler=Hydrate</c> is the one round trip <c>razor-sync.ts</c> uses to restore a snapshot from
///     <c>localStorage</c> on a cold visit — the only handler that also seeds <c>ChosenALevels</c>, since
///     every other facts-editing handler deliberately leaves the basket untouched.
/// </summary>
public sealed class HydrateHandlerTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public HydrateHandlerTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task A_cold_visit_with_no_state_cookie_renders_an_empty_page_flagged_for_rehydration()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		html.Should().Contain("data-empty=\"true\"");
		html.Should().Contain("data-cleared=\"false\"");
	}

	[Fact]
	public async Task Hydrating_restores_facts_and_the_basket_in_one_round_trip()
	{
		using var client = factory.CreateClient(new() {
			AllowAutoRedirect = false,
		});

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		// examples/golden/strong-constraints.json's GCSEs: with no chosen A-levels, French is green.
		var form = new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
			["Hobbies[0]"] = "chess_club",
			["chosenALevels[0]"] = "french",
		};
		var gcses = new (string Subject, int Grade)[] {
			("maths", 8), ("english_language", 8), ("english_literature", 8), ("physics", 8), ("chemistry", 8), ("biology", 8), ("french", 8), ("german", 8), ("physical_education", 8), ("computer_studies", 8), ("history", 8), ("music", 8), ("art", 8),
		};
		for (var i = 0; i < gcses.Length; ++i) {
			form[$"Gcses[{i}].Subject"] = gcses[i].Subject;
			form[$"Gcses[{i}].Grade"] = gcses[i].Grade.ToString(CultureInfo.InvariantCulture);
		}

		using var hydrateContent = new FormUrlEncodedContent(form);
		using var hydrateResponse = await client.PostAsync(new Uri("/razor?handler=Hydrate", UriKind.Relative), hydrateContent);
		hydrateResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

		using var afterHydrate = await client.GetAsync(hydrateResponse.Headers.Location);
		var html = await afterHydrate.Content.ReadAsStringAsync();

		html.Should().Contain("2009-09-01");
		html.Should().Contain("chess_club");
		html.Should().Contain("list-inline-item badge text-bg-primary rounded-pill\">French");
		html.Should().Contain("data-empty=\"false\"");
	}

	[Fact]
	public async Task Starting_over_redirects_with_cleared_true_so_the_next_render_does_not_rehydrate()
	{
		using var client = factory.CreateClient(new() {
			AllowAutoRedirect = false,
		});

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		using var saveContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = "8",
		});
		using var saveResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), saveContent);
		using var afterSave = await client.GetAsync(saveResponse.Headers.Location);
		var resetToken = await ExtractAntiForgeryTokenAsync(afterSave);

		using var resetContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = resetToken,
		});
		using var resetResponse = await client.PostAsync(new Uri("/razor?handler=Reset", UriKind.Relative), resetContent);

		resetResponse.Headers.Location!.OriginalString.Should().Contain("cleared=True");

		using var afterReset = await client.GetAsync(resetResponse.Headers.Location);
		var html = await afterReset.Content.ReadAsStringAsync();
		html.Should().Contain("data-empty=\"true\"");
		html.Should().Contain("data-cleared=\"true\"");
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
