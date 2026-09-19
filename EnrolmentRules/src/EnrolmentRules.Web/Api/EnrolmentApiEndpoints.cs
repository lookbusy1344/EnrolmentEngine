namespace EnrolmentRules.Web.Api;

using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Domain;
using Engine;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Services;

/// <summary>Maps the stateless <c>/api/enrolment/*</c> endpoints the Vue client calls; no session, no cookies.</summary>
public static class EnrolmentApiEndpoints
{
	private const string ForwardedForHeaderName = "X-Forwarded-For";

	/// <summary>
	///     Kestrel's request-body limit for <c>POST /api/enrolment/evaluate</c> (applied by
	///     <see cref="UseEnrolmentEvaluateRequestSizeLimit" />): comfortably larger than any realistic posted
	///     snapshot (the domain-count/token-length caps in <see cref="EnrolmentApiBoundaryValidator" /> already
	///     bound that far tighter) but bounded, so a request cannot make the anonymous, CPU-bound,
	///     single-instance endpoint buffer an unbounded body before those checks even run. Exceeding it fails
	///     the read itself with a 413, before model binding or <see cref="EnrolmentApiBoundaryValidator" />
	///     ever executes.
	/// </summary>
	public const long MaxEvaluateRequestBodyBytes = 32 * 1024;

	/// <summary>Fixed-window rate limiter policy name applied to the whole <c>/api/enrolment</c> group (F15).</summary>
	public const string RateLimiterPolicyName = "enrolment-api";

	/// <summary>
	///     Requests a single client (partitioned by remote IP) may make within <see cref="RateLimitWindow" />
	///     before a 429. Still bounds sustained single-client abuse of the anonymous, CPU-bound endpoint, but
	///     high enough that Playwright's fully-parallel e2e suite — many browser workers sharing one loopback
	///     IP against one server instance — never trips it; a tighter per-request-cost limit belongs behind a
	///     reverse proxy that can see real client IPs, not this in-process fallback.
	/// </summary>
	public const int RateLimitPermitLimit = 1000;

	/// <summary>The fixed-window rate limiter's window length.</summary>
	public static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);

	/// <summary>
	///     Caps the Kestrel request-body size for <c>POST /api/enrolment/evaluate</c> only. Must run as
	///     ordinary middleware ahead of routing/model binding — a minimal-API endpoint filter runs too late:
	///     the JSON body is already bound into the filter's arguments before the filter delegate executes.
	/// </summary>
	public static IApplicationBuilder UseEnrolmentEvaluateRequestSizeLimit(this IApplicationBuilder app) =>
		app.Use(async (context, next) => {
			if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/enrolment/evaluate")) {
				var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
				if (bodySizeFeature is { IsReadOnly: false }) {
					bodySizeFeature.MaxRequestBodySize = MaxEvaluateRequestBodyBytes;
				}
			}

			await next(context);
		});

	/// <summary>
	///     Registers the fixed-window limiter used by <see cref="MapEnrolmentApi" />: a per-client-IP cap so
	///     the anonymous, CPU-bound <c>/api/enrolment/*</c> group cannot be driven at an unbounded request
	///     rate (F15) — the body-size cap and boundary validator bound the cost of one request, not the rate.
	/// </summary>
	public static IServiceCollection AddEnrolmentApiRateLimiting(this IServiceCollection services) => services.AddEnrolmentApiRateLimiting(RateLimitClientAddressSource.Direct);

	internal static IServiceCollection AddEnrolmentApiRateLimiting(
		this IServiceCollection services, RateLimitClientAddressSource clientAddressSource)
	{
		ArgumentNullException.ThrowIfNull(services);
		return services.AddRateLimiter(options => {
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.OnRejected = static (context, cancellationToken) => {
				context.HttpContext.Response.Headers.RetryAfter = ((int)RateLimitWindow.TotalSeconds).ToString(CultureInfo.InvariantCulture);
				return ValueTask.CompletedTask;
			};
			_ = options.AddPolicy(RateLimiterPolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
				ClientPartitionKey(httpContext, clientAddressSource),
				static _ => new() {
					PermitLimit = RateLimitPermitLimit,
					Window = RateLimitWindow,
					QueueLimit = 0,
				}));
		});
	}

	/// <summary>
	///     Resolve the stable client address used to partition the limiter. Google's external Application Load Balancer
	///     appends <c>&lt;client&gt;, &lt;load-balancer&gt;</c> to any caller-supplied forwarding chain, so the
	///     second-to-last address is authoritative only when the host explicitly opts into that trusted topology;
	///     direct hosts ignore the untrusted header.
	/// </summary>
	internal static string ClientPartitionKey(HttpContext context, RateLimitClientAddressSource source)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (source == RateLimitClientAddressSource.GoogleCloudExternalLoadBalancer) {
			var addresses = context.Request.Headers[ForwardedForHeaderName]
								   .ToString()
								   .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			if (addresses.Length >= 2 && IPAddress.TryParse(addresses[^2], out var clientAddress)) {
				return clientAddress.ToString();
			}
		}

		return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
	}

	public static IEndpointRouteBuilder MapEnrolmentApi(this IEndpointRouteBuilder endpoints)
	{
		ArgumentNullException.ThrowIfNull(endpoints);

		var group = endpoints.MapGroup("/api/enrolment")
							 .RequireRateLimiting(RateLimiterPolicyName)
							 .AddEndpointFilter(async (context, next) => {
								 context.HttpContext.Response.Headers.CacheControl = "no-store";
								 return await next(context);
							 });

		_ = group.MapGet("/options", GetOptions).WithName("GetEnrolmentOptions");
		_ = group.MapPost("/evaluate", Evaluate).WithName("EvaluateEnrolment");

		return endpoints;
	}

	private static Results<Ok<EnrolmentOptionsResponse>, ProblemHttpResult> GetOptions(
		string? policy,
		IEnrolmentPolicyRegistry registry,
		EnrolmentOptionsServiceCache optionsServices)
	{
		if (!EnrolmentPolicySelector.TryResolve(registry, policy, out var selected)) {
			return UnknownPolicyProblem(policy, registry);
		}

		var options = optionsServices.Get(selected);
		return TypedResults.Ok(EnrolmentOptionsResponseFactory.Create(options, registry.Descriptors));
	}

	private static Results<Ok<EnrolmentEvaluateResponse>, ProblemHttpResult> Evaluate(
		EnrolmentEvaluateRequest request, string? policy, IEnrolmentPolicyRegistry registry, CancellationToken cancellationToken)
	{
		if (!EnrolmentPolicySelector.TryResolve(registry, policy, out var selected)) {
			return UnknownPolicyProblem(policy, registry);
		}

		var boundaryErrors = EnrolmentApiBoundaryValidator.Validate(
			request, ((IEnrolmentEvaluator)selected.Engine).Catalogue, selected.Engine.Gcses);
		if (boundaryErrors.Count > 0) {
			var rejected = new ValidatedEvaluation<PolicyComparisonResult>(new([.. boundaryErrors]), null);
			return TypedResults.Ok(EnrolmentEvaluateResponseFactory.Create(rejected));
		}

		if (!EnrolmentApiMapper.TryToStudentInput(request, out var input)) {
			return TypedResults.Problem(
				statusCode: StatusCodes.Status400BadRequest,
				title: "Invalid enrolment snapshot.",
				detail: "Could not map the posted snapshot: an unrecognised prior-qualification type or chosen A-level subject value.");
		}

		var comparison = registry.Compare(selected.Descriptor.Id, input, cancellationToken);
		return TypedResults.Ok(EnrolmentEvaluateResponseFactory.Create(comparison));
	}

	private static string UnknownPolicyMessage(string? policy, IEnrolmentPolicyRegistry registry) =>
		$"Unknown policy '{policy}'. Available: {string.Join(", ", registry.Descriptors.Select(static d => d.Id.Value))}.";

	private static ProblemHttpResult UnknownPolicyProblem(string? policy, IEnrolmentPolicyRegistry registry) =>
		TypedResults.Problem(
			statusCode: StatusCodes.Status400BadRequest,
			title: "Unknown enrolment policy.",
			detail: UnknownPolicyMessage(policy, registry));
}

/// <summary>The trusted source from which the rate limiter derives a client address.</summary>
internal enum RateLimitClientAddressSource
{
	Direct,
	GoogleCloudExternalLoadBalancer,
}
