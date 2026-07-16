namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;

public sealed class RemoveRowTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public RemoveRowTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Removing_a_gcse_row_drops_it_from_the_saved_snapshot()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		// A real browser submits every field currently in the form regardless of which button (or
		// formaction override) triggered the submit, so both posts below carry the same full state.
		using var saveContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = "8",
			["Gcses[1].Subject"] = "physics",
			["Gcses[1].Grade"] = "7",
		});
		using var saveResponse = await client.PostAsync(new Uri("/?handler=SaveFacts", UriKind.Relative), saveContent);
		using var afterSave = await client.GetAsync(saveResponse.Headers.Location);
		var htmlAfterSave = await afterSave.Content.ReadAsStringAsync();
		htmlAfterSave.Should().Contain("value=\"maths\" selected").And.Contain("value=\"physics\" selected");

		var removeToken = await ExtractAntiForgeryTokenAsync(afterSave);
		using var removeContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = removeToken,
			["DateOfBirth"] = "2009-09-01",
			["Gcses[0].Subject"] = "maths",
			["Gcses[0].Grade"] = "8",
			["Gcses[1].Subject"] = "physics",
			["Gcses[1].Grade"] = "7",
		});
		using var removeResponse = await client.PostAsync(new Uri("/?handler=RemoveGcseRow&index=0", UriKind.Relative), removeContent);
		using var afterRemove = await client.GetAsync(removeResponse.Headers.Location);
		var htmlAfterRemove = await afterRemove.Content.ReadAsStringAsync();

		htmlAfterRemove.Should().NotContain("value=\"maths\" selected");
		htmlAfterRemove.Should().Contain("value=\"physics\" selected");
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
