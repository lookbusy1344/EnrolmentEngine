namespace EnrolmentRules.Web.Tests;

using System.Net;
using AwesomeAssertions;

public sealed class SmokeTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public SmokeTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Get_index_returns_200_with_primary_form()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain("<form");
		html.Should().Contain("SaveFacts");
	}
}
