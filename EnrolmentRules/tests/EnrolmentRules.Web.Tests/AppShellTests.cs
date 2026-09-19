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

	[Fact]
	public async Task Get_app_allows_pinch_zoom_via_the_shared_layouts_viewport_meta_tag()
	{
		// The standard width=device-width, initial-scale=1 viewport with no maximum-scale/
		// user-scalable restriction, which would otherwise block pinch zoom for low-vision users.
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/app", UriKind.Relative));
		var html = await response.Content.ReadAsStringAsync();

		html.Should().MatchRegex("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"\\s*/>");
		html.Should().NotContain("maximum-scale");
		html.Should().NotContain("user-scalable");
	}

	/// <summary>F15: baseline hardening headers on every response, not just the API.</summary>
	[Fact]
	public async Task Get_app_carries_the_baseline_security_headers()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/app", UriKind.Relative));

		response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
		response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("strict-origin-when-cross-origin");
		var csp = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Which;
		csp.Should().Contain("default-src 'self'");
		csp.Should().Contain("fonts.googleapis.com");
		csp.Should().Contain("fonts.gstatic.com");
	}
}
