namespace EnrolmentRules.Web.Tests;

using System.Globalization;
using AwesomeAssertions;

public sealed class RenderExplanationsTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public RenderExplanationsTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Saving_a_known_valid_student_renders_green_amber_and_red_explanations()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		// examples/golden/strong-constraints.json — a known-eligible student with a stable mix of ratings.
		var form = new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
			["Hobbies[0]"] = "chess_club",
		};
		var gcses = new (string Subject, int Grade)[] {
			("maths", 8), ("english_language", 8), ("english_literature", 8), ("physics", 8), ("chemistry", 8), ("biology", 8), ("french", 8),
			("german", 8), ("physical_education", 8), ("computer_studies", 8), ("history", 8), ("music", 8), ("art", 8),
		};
		for (var i = 0; i < gcses.Length; i++) {
			form[$"Gcses[{i}].Subject"] = gcses[i].Subject;
			form[$"Gcses[{i}].Grade"] = gcses[i].Grade.ToString(CultureInfo.InvariantCulture);
		}

		using var content = new FormUrlEncodedContent(form);
		using var postResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), content);
		using var followUp = await client.GetAsync(postResponse.Headers.Location);
		var html = await followUp.Content.ReadAsStringAsync();

		// From the committed golden: physics is green, art is amber, further_maths is red.
		html.Should().Contain("physics").And.Contain("Green");
		html.Should().Contain("art").And.Contain("Amber");
		html.Should().Contain("further_maths").And.Contain("Red");
		html.Should().Contain("Entry met"); // a deciding reason from TryExplain
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
