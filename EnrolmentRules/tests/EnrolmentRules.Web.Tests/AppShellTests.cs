namespace EnrolmentRules.Web.Tests;

using System.Net;
using AwesomeAssertions;

public sealed class AppShellTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public AppShellTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Get_app_returns_the_shared_layout_and_mount_point()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/app", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain("Green&nbsp;Shoots"); // the shared _Layout brand, proving /app reuses it
		html.Should().Contain("<div id=\"enrolment-vue-app\"></div>");
	}

	[Fact]
	public async Task Get_app_references_the_built_asset_path_from_the_vite_manifest()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/app", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		html.Should().MatchRegex("<script type=\"module\" src=\"/app/assets/main-[A-Za-z0-9_-]+\\.js\"></script>");
	}
}
