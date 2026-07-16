namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;

/// <summary>
///     Every facts-editing/basket postback redirects with a URL fragment so the reload lands back at the section the user was editing, not the page
///     top.
/// </summary>
public sealed class RedirectFragmentTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public RedirectFragmentTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Saving_facts_via_the_main_button_redirects_to_the_results_section()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		using var getResponse = await client.GetAsync(new Uri("/", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = "8",
		});
		using var response = await client.PostAsync(new Uri("/?handler=SaveFacts", UriKind.Relative), content);

		response.Headers.Location!.OriginalString.Should().EndWith("#results-heading");
	}

	[Theory]
	[InlineData("gcse-section")]
	[InlineData("qualifications-section")]
	[InlineData("hobbies-section")]
	public async Task Saving_facts_via_a_section_add_button_redirects_back_to_that_section(string sectionFragment)
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		using var getResponse = await client.GetAsync(new Uri("/", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = "8",
		});
		using var response = await client.PostAsync(new Uri($"/?handler=SaveFacts&fragment={sectionFragment}", UriKind.Relative), content);

		response.Headers.Location!.OriginalString.Should().EndWith($"#{sectionFragment}");
	}

	[Fact]
	public async Task Removing_a_gcse_row_redirects_back_to_the_gcse_section()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		using var getResponse = await client.GetAsync(new Uri("/", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = "8",
		});
		using var response = await client.PostAsync(new Uri("/?handler=RemoveGcseRow&index=0", UriKind.Relative), content);

		response.Headers.Location!.OriginalString.Should().EndWith("#gcse-section");
	}

	[Fact]
	public async Task Removing_a_prior_qualification_row_redirects_back_to_the_qualifications_section()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		using var getResponse = await client.GetAsync(new Uri("/", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["PriorQualifications[0].Subject"] = "English",
			["PriorQualifications[0].Type"] = "Gcse",
			["PriorQualifications[0].Grade"] = "7",
		});
		using var response = await client.PostAsync(new Uri("/?handler=RemoveQualificationRow&index=0", UriKind.Relative), content);

		response.Headers.Location!.OriginalString.Should().EndWith("#qualifications-section");
	}

	[Fact]
	public async Task Removing_a_hobby_row_redirects_back_to_the_hobbies_section()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		using var getResponse = await client.GetAsync(new Uri("/", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["Hobbies[0]"] = "chess_club",
		});
		using var response = await client.PostAsync(new Uri("/?handler=RemoveHobbyRow&index=0", UriKind.Relative), content);

		response.Headers.Location!.OriginalString.Should().EndWith("#hobbies-section");
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
