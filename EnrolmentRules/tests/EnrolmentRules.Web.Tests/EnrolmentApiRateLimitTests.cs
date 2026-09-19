namespace EnrolmentRules.Web.Tests;

using System.Net;
using Api;
using AwesomeAssertions;

/// <summary>
///     <c>/api/enrolment/*</c> is anonymous, unauthenticated and does real work per call (F15) — the
///     body-size cap and boundary validator bound the cost of one request, not the request rate. A single
///     <see cref="WebAppFactory" /> instance is used only by this class (never shared via a collection
///     fixture), so the fixed-window limiter's per-client counter starts fresh for the test.
/// </summary>
public sealed class EnrolmentApiRateLimitTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public EnrolmentApiRateLimitTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Rejects_the_client_with_429_once_the_window_limit_is_exceeded()
	{
		using var client = factory.CreateClient();

		HttpResponseMessage? last = null;
		for (var i = 0; i < EnrolmentApiEndpoints.RateLimitPermitLimit + 1; ++i) {
			last?.Dispose();
			last = await client.GetAsync(new Uri("/api/enrolment/options", UriKind.Relative));
		}

		using var response = last!;
		response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		response.Headers.RetryAfter.Should().NotBeNull();
	}

	[Fact]
	public void External_Google_load_balancer_partition_uses_the_appended_client_address_not_a_spoofed_prefix()
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");
		context.Request.Headers["X-Forwarded-For"] = "203.0.113.99, 198.51.100.24, 192.0.2.10";

		var key = EnrolmentApiEndpoints.ClientPartitionKey(
			context, RateLimitClientAddressSource.GoogleCloudExternalLoadBalancer);

		key.Should().Be("198.51.100.24");
	}

	[Fact]
	public void Direct_partition_ignores_untrusted_forwarded_addresses()
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.30");
		context.Request.Headers["X-Forwarded-For"] = "203.0.113.99, 198.51.100.24, 192.0.2.10";

		var key = EnrolmentApiEndpoints.ClientPartitionKey(context, RateLimitClientAddressSource.Direct);

		key.Should().Be("198.51.100.30");
	}

	[Fact]
	public void Rate_limit_client_address_source_defaults_to_direct_even_on_Cloud_Run()
	{
		using var configuration = new ConfigurationManager();
		configuration["K_SERVICE"] = "enrolment-web";

		Program.ResolveRateLimitClientAddressSource(configuration).Should().Be(RateLimitClientAddressSource.Direct);
	}

	[Fact]
	public void External_Google_load_balancer_trust_requires_explicit_configuration()
	{
		using var configuration = new ConfigurationManager();
		configuration[Program.RateLimitClientAddressSourceConfigurationKey] = "GoogleCloudExternalLoadBalancer";

		Program.ResolveRateLimitClientAddressSource(configuration)
			   .Should().Be(RateLimitClientAddressSource.GoogleCloudExternalLoadBalancer);
	}
}
