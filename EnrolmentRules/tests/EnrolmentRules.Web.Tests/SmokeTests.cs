namespace EnrolmentRules.Web.Tests;

using System.Net;
using AwesomeAssertions;

public sealed class SmokeTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public SmokeTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Get_index_serves_the_vue_app_directly()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain("id=\"enrolment-vue-app\"");
	}

	[Fact]
	public async Task Get_app_returns_200_with_the_vue_mount_point()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/app", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain("id=\"enrolment-vue-app\"");
	}
}
