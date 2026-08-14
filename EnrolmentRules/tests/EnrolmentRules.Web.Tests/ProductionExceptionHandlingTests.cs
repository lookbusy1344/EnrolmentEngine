namespace EnrolmentRules.Web.Tests;

using System.Net;
using AwesomeAssertions;
using Domain;
using Engine;
using Microsoft.AspNetCore.Mvc;
using Services;

/// <summary>
///     Production only wraps the pipeline in <c>UseExceptionHandler</c> (see <c>Program.Main</c>); the
///     default-configured <see cref="WebAppFactory" /> runs as Development, so a deliberately-throwing
///     service is combined with a Production-environment factory here to exercise that handler for real,
///     across both an HTML page request and a JSON <c>/api/*</c> request.
/// </summary>
public sealed class ProductionExceptionHandlingTests
{
	[Fact]
	public async Task A_page_request_that_throws_gets_a_safe_500_with_no_exception_detail()
	{
		using var baseFactory = new WebAppFactory();
		using var factory = baseFactory.WithWebHostBuilder(builder => builder
			.UseEnvironment("Production")
			.ConfigureServices(services => services.AddSingleton<IViteManifestReader>(new ThrowingViteManifestReader())));
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/app", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		response.Headers.CacheControl!.NoStore.Should().BeTrue();
		var body = await response.Content.ReadAsStringAsync();
		body.Should().NotContain("ThrowingViteManifestReader");
		body.Should().NotContain("at EnrolmentRules.Web");
	}

	[Fact]
	public async Task An_api_request_that_throws_gets_a_safe_500_problem_json_response()
	{
		using var baseFactory = new WebAppFactory();
		using var factory = baseFactory.WithWebHostBuilder(builder => builder
			.UseEnvironment("Production")
			.ConfigureServices(services => services.AddSingleton<IEnrolmentEngine>(new ThrowingEnrolmentEngine())));
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/api/enrolment/options", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		response.Headers.CacheControl!.NoStore.Should().BeTrue();
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
		var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
		problem.Should().NotBeNull();
		problem!.Status.Should().Be(StatusCodes.Status500InternalServerError);
		var body = await response.Content.ReadAsStringAsync();
		body.Should().NotContain("ThrowingEnrolmentEngine");
		body.Should().NotContain("at EnrolmentRules.Web");
	}

	private sealed class ThrowingViteManifestReader : IViteManifestReader
	{
		public ViteAssetPaths GetEntryAssets(string entrySourcePath) =>
			throw new InvalidOperationException("ThrowingViteManifestReader: simulated manifest failure.");
	}

	private sealed class ThrowingEnrolmentEngine : IEnrolmentEngine
	{
		public CatalogueData Catalogue => throw Failure();

		public QualificationScale Scale => throw Failure();

		public PolicyThresholds Thresholds => throw Failure();

		public EnrolmentResult Evaluate(StudentInput student, CancellationToken cancellationToken = default) => throw Failure();

		public EnrolmentResult Evaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ExplainedResult Explain(StudentInput student, CancellationToken cancellationToken = default) => throw Failure();

		public ExplainedResult Explain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<EnrolmentResult> EvaluateValidated(StudentInput student, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<EnrolmentResult> EvaluateValidated(
			StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<ExplainedResult> ExplainValidated(StudentInput student, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<ExplainedResult> ExplainValidated(
			StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			throw Failure();

		public IReadOnlyList<Subject> StaleChoices(StudentInput student, CancellationToken cancellationToken = default) =>
			throw Failure();

		public AdviceResult Advise(StudentInput student, CancellationToken cancellationToken = default) => throw Failure();

		public AdviceResult Advise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			throw Failure();

		public AdviceResult Advise(StudentInput student, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default) =>
			throw Failure();

		public AdviceResult Advise(
			StudentInput student, DateOnly asOf, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<AdviceResult> AdviseValidated(StudentInput student, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<AdviceResult> AdviseValidated(
			StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<AdviceResult> AdviseValidated(
			StudentInput student, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default) =>
			throw Failure();

		public ValidatedEvaluation<AdviceResult> AdviseValidated(
			StudentInput student, DateOnly asOf, UnsatGcseAdvice unsatGcses, CancellationToken cancellationToken = default) =>
			throw Failure();

		public SubjectCriteria Describe(Subject subject) => throw Failure();

		private static InvalidOperationException Failure() =>
			new("ThrowingEnrolmentEngine: simulated engine failure.");
	}
}
