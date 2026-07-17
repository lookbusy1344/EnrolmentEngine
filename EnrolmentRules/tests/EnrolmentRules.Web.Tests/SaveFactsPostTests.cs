namespace EnrolmentRules.Web.Tests;

using System.Net;
using AwesomeAssertions;

public sealed class SaveFactsPostTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public SaveFactsPostTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Posting_save_facts_persists_the_snapshot_and_renders_it_back_after_redirect()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var antiForgeryToken = await ExtractAntiForgeryTokenAsync(getResponse);

		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = antiForgeryToken,
			["DateOfBirth"] = "2009-06-01",
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = "8",
			["PriorQualifications[0].Subject"] = "English",
			["PriorQualifications[0].Type"] = "Gcse",
			["PriorQualifications[0].Grade"] = "7",
			["Hobbies[0]"] = "chess_club",
		});

		using var postResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), content);
		postResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

		using var followUp = await client.GetAsync(postResponse.Headers.Location);
		var html = await followUp.Content.ReadAsStringAsync();

		html.Should().Contain("2009-06-01");
		html.Should().Contain("maths");
		html.Should().Contain("chess_club");
		html.Should().Contain("English");
	}

	[Theory]
	[InlineData("47", "9")]
	[InlineData("0", "1")]
	[InlineData("-3", "1")]
	[InlineData("7.6", "8")]
	[InlineData("7.4", "7")]
	public async Task Posting_a_grade_off_the_scale_normalises_it_before_it_reaches_the_session(string posted, string expected)
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var antiForgeryToken = await ExtractAntiForgeryTokenAsync(getResponse);

		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = antiForgeryToken,
			["DateOfBirth"] = "2009-06-01",
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = posted,
		});

		using var postResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), content);
		postResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

		using var followUp = await client.GetAsync(postResponse.Headers.Location);
		var html = await followUp.Content.ReadAsStringAsync();

		html.Should().Contain($"name=\"Gcses[0].Grade\" class=\"form-control\" value=\"{expected}\"");
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
