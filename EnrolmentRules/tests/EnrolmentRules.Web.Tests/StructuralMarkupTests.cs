namespace EnrolmentRules.Web.Tests;

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Domain;

public sealed partial class StructuralMarkupTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public StructuralMarkupTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Index_page_has_exactly_one_h1_and_section_headings()
	{
		var html = await GetIndexHtmlAsync();

		H1Pattern().Count(html).Should().Be(1);
		H2Pattern().Count(html).Should().BeGreaterThanOrEqualTo(2, "facts and results each need their own heading");
	}

	[Fact]
	public async Task Every_visible_text_number_or_date_input_has_a_label()
	{
		var html = await GetIndexHtmlAsync();

		var visibleInputs = VisibleInputIdPattern().Matches(html).Select(static m => m.Groups[1].Value).ToList();
		var labelledIds = LabelForPattern().Matches(html).Select(static m => m.Groups[1].Value).ToHashSet();

		visibleInputs.Should().NotBeEmpty();
		foreach (var id in visibleInputs) {
			labelledIds.Should().Contain(id, $"input #{id} must have a matching <label for=\"{id}\">");
		}
	}

	[Fact]
	public async Task Every_hidden_subject_field_sits_inside_a_real_submit_button_form()
	{
		var html = await GetIndexHtmlWithResultsAsync();

		var subjectForms = SubjectFormPattern().Matches(html);
		subjectForms.Should().NotBeEmpty();
		foreach (Match match in subjectForms) {
			match.Value.Should().Contain("<button type=\"submit\"");
		}
	}

	[Fact]
	public async Task Unavailable_subject_controls_are_tied_to_their_reason_text()
	{
		var html = await GetIndexHtmlWithResultsAsync();

		UnavailableButtonPattern().Matches(html).Should().NotBeEmpty();
		foreach (Match match in UnavailableButtonPattern().Matches(html)) {
			var reasonId = match.Groups[1].Value;
			html.Should().Contain($"id=\"{reasonId}\"");
		}
	}

	[Fact]
	public async Task Footer_shows_the_build_stamp_as_plain_text()
	{
		var html = await GetIndexHtmlAsync();

		// Razor HTML-encodes the '+' separating version from commit metadata.
		html.Should().Contain(HtmlEncoder.Default.Encode(BuildInfo.VersionWithCommit),
			"visitors need to know which build answered them");
		html.Should().NotContain($"https://github.com/lookbusy1344/EnrolmentEngine/commit/{BuildInfo.Commit}",
			"the build stamp is plain text, not a link to the commit");
	}

	private async Task<string> GetIndexHtmlAsync()
	{
		using var client = factory.CreateClient();
		using var response = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		return await response.Content.ReadAsStringAsync();
	}

	/// <summary>Saves a known-eligible student first, so the results section (and its choose/remove forms) actually renders.</summary>
	private async Task<string> GetIndexHtmlWithResultsAsync()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		// examples/golden/strong-constraints.json — a known-eligible student.
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
		return await followUp.Content.ReadAsStringAsync();
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

	[GeneratedRegex("<h1[ >]")]
	private static partial Regex H1Pattern();

	[GeneratedRegex("<h2[ >]")]
	private static partial Regex H2Pattern();

	[GeneratedRegex("<input type=\"(?:text|number|date)\" id=\"([^\"]+)\"")]
	private static partial Regex VisibleInputIdPattern();

	[GeneratedRegex("<label for=\"([^\"]+)\"")]
	private static partial Regex LabelForPattern();

	[GeneratedRegex("<form method=\"post\"[^>]*>\\s*<input type=\"hidden\" name=\"subject\"[^>]*/>.*?</form>", RegexOptions.Singleline)]
	private static partial Regex SubjectFormPattern();

	[GeneratedRegex("<button type=\"button\"[^>]*disabled[^>]*aria-describedby=\"([^\"]+)\"[^>]*>\\s*Unavailable\\s*</button>")]
	private static partial Regex UnavailableButtonPattern();
}
