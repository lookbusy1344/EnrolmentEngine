namespace EnrolmentRules.Web.Tests;

using System.Globalization;
using AwesomeAssertions;
using Domain;

public sealed class AgeDisplayTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public AgeDisplayTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Displays_the_calculated_age_for_the_saved_date_of_birth()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		var dob = new DateOnly(1990, 6, 15);
		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		});

		using var postResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), content);
		using var followUp = await client.GetAsync(postResponse.Headers.Location);
		var html = await followUp.Content.ReadAsStringAsync();

		var expectedAge = AgeCalculator.WholeYears(dob, DateOnly.FromDateTime(DateTime.UtcNow));
		html.Should().Contain("id=\"DateOfBirthAge\"");
		html.Should().Contain($"Age: {expectedAge}");
	}

	[Fact]
	public async Task Displays_an_age_for_the_pre_filled_default_date_of_birth_before_anything_is_saved()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		html.Should().Contain("id=\"DateOfBirthAge\"");
		html.Should().MatchRegex("Age: \\d+");
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
