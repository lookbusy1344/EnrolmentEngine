namespace EnrolmentRules.Web.Tests;

using System.Net;
using AwesomeAssertions;

public sealed class SmokeTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public SmokeTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Get_index_redirects_to_razor_by_default()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var response = await client.GetAsync(new Uri("/", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.OriginalString.Should().Be("/razor");
	}

	[Fact]
	public async Task Get_razor_returns_200_with_primary_form()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain("<form");
		html.Should().Contain("SaveFacts");
	}
}
