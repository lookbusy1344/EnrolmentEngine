namespace EnrolmentRules.Web.Tests;

using Api;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http.Features;

/// <summary>
///     <see cref="EnrolmentApiEndpoints.UseEnrolmentEvaluateRequestSizeLimit" /> caps Kestrel's request-body
///     size via <see cref="IHttpMaxRequestBodySizeFeature" /> — a limit <c>WebApplicationFactory</c>'s
///     in-memory <c>TestServer</c> does not itself enforce (it has no real transport-level body reader to
///     interrupt), so the middleware's wiring is verified directly against a fake feature rather than by
///     posting an oversized body through the full HTTP pipeline and expecting a 413.
/// </summary>
public sealed class EnrolmentApiRequestSizeLimitTests
{
	[Fact]
	public async Task Sets_the_configured_limit_for_a_post_to_evaluate()
	{
		var feature = new FakeMaxRequestBodySizeFeature();
		var context = ContextFor("POST", "/api/enrolment/evaluate", feature);

		await BuildPipeline()(context);

		feature.MaxRequestBodySize.Should().Be(EnrolmentApiEndpoints.MaxEvaluateRequestBodyBytes);
	}

	[Fact]
	public async Task Leaves_the_limit_untouched_for_a_get_to_options()
	{
		var feature = new FakeMaxRequestBodySizeFeature();
		var context = ContextFor("GET", "/api/enrolment/options", feature);

		await BuildPipeline()(context);

		feature.MaxRequestBodySize.Should().BeNull();
	}

	[Fact]
	public async Task Leaves_the_limit_untouched_when_the_feature_is_already_read_only()
	{
		var feature = new FakeMaxRequestBodySizeFeature { IsReadOnly = true };
		var context = ContextFor("POST", "/api/enrolment/evaluate", feature);

		await BuildPipeline()(context);

		feature.MaxRequestBodySize.Should().BeNull();
	}

	private static RequestDelegate BuildPipeline()
	{
		var appBuilder = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
		_ = appBuilder.UseEnrolmentEvaluateRequestSizeLimit();
		appBuilder.Run(static _ => Task.CompletedTask);
		return appBuilder.Build();
	}

	private static DefaultHttpContext ContextFor(string method, string path, IHttpMaxRequestBodySizeFeature feature)
	{
		var context = new DefaultHttpContext();
		context.Request.Method = method;
		context.Request.Path = path;
		context.Features.Set(feature);
		return context;
	}

	private sealed class FakeMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
	{
		public bool IsReadOnly { get; init; }

		public long? MaxRequestBodySize { get; set; }
	}
}
