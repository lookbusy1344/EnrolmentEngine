namespace EnrolmentRules.Web.Api;

using Domain;
using Engine;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Services;

/// <summary>Maps the stateless <c>/api/enrolment/*</c> endpoints the Vue client calls; no session, no cookies.</summary>
public static class EnrolmentApiEndpoints
{
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

	public static IEndpointRouteBuilder MapEnrolmentApi(this IEndpointRouteBuilder endpoints)
	{
		ArgumentNullException.ThrowIfNull(endpoints);

		var group = endpoints.MapGroup("/api/enrolment").AddEndpointFilter(async (context, next) => {
			context.HttpContext.Response.Headers.CacheControl = "no-store";
			return await next(context);
		});

		_ = group.MapGet("/options", GetOptions).WithName("GetEnrolmentOptions");
		_ = group.MapPost("/evaluate", Evaluate).WithName("EvaluateEnrolment");

		return endpoints;
	}

	private static Results<Ok<EnrolmentOptionsResponse>, ProblemHttpResult> GetOptions(
		string? policy, IEnrolmentPolicyRegistry registry, TimeProvider timeProvider)
	{
		if (!EnrolmentPolicySelector.TryResolve(registry, policy, out var selected)) {
			return UnknownPolicyProblem(policy, registry);
		}

		var options = new EnrolmentOptionsService(selected, timeProvider);
		return TypedResults.Ok(EnrolmentOptionsResponseFactory.Create(options, registry.Descriptors));
	}

	private static Results<Ok<EnrolmentEvaluateResponse>, ProblemHttpResult> Evaluate(
		EnrolmentEvaluateRequest request, string? policy, IEnrolmentPolicyRegistry registry, CancellationToken cancellationToken)
	{
		if (!EnrolmentPolicySelector.TryResolve(registry, policy, out var selected)) {
			return UnknownPolicyProblem(policy, registry);
		}

		var boundaryErrors = EnrolmentApiBoundaryValidator.Validate(request, ((IEnrolmentEvaluator)selected.Engine).Catalogue);
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
