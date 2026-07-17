namespace EnrolmentRules.Web.Tests;

using System.Net;
using AwesomeAssertions;

/// <summary>
///     Covers the <c>EnrolmentWeb:DefaultExperience = Vue</c> branch that <see cref="SmokeTests" />'s
///     default-configured <see cref="WebAppFactory" /> can't reach, by overriding configuration on a
///     private factory instance instead.
/// </summary>
public sealed class DefaultExperienceRoutingTests
{
	[Fact]
	public async Task Get_index_redirects_to_app_when_vue_is_the_configured_default()
	{
		using var baseFactory = new WebAppFactory();
		using var factory = baseFactory.WithWebHostBuilder(builder =>
			builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection([new("EnrolmentWeb:DefaultExperience", "Vue")])));
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var response = await client.GetAsync(new Uri("/", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.OriginalString.Should().Be("/app");
	}
}
