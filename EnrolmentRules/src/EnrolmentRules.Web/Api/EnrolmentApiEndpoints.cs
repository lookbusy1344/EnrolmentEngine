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

	private static Ok<EnrolmentOptionsResponse> GetOptions(EnrolmentOptionsService options) =>
		TypedResults.Ok(EnrolmentOptionsResponseFactory.Create(options));

	private static Results<Ok<EnrolmentEvaluateResponse>, BadRequest<string>> Evaluate(
		EnrolmentEvaluateRequest request, IEnrolmentEngine engine, CancellationToken cancellationToken)
	{
		var boundaryErrors = EnrolmentApiBoundaryValidator.Validate(request, ((IEnrolmentEvaluator)engine).Catalogue);
		if (boundaryErrors.Count > 0) {
			var rejected = new ValidatedEvaluation<ExplainedResult>(new([.. boundaryErrors]), null);
			return TypedResults.Ok(EnrolmentEvaluateResponseFactory.Create(rejected, []));
		}

		if (!EnrolmentApiMapper.TryToStudentInput(request, out var input)) {
			return TypedResults.BadRequest(
				"Could not map the posted snapshot: an unrecognised prior-qualification type or chosen A-level subject value.");
		}

		var evaluation = engine.ExplainValidated(input, cancellationToken);

		// On the rejection path ExplainValidated has already found the stale choices internally but discards them, so
		// StaleChoices re-runs the pipeline once to recover the subjects to eject. The cost lands only when a
		// committed choice has gone red (a rare, already-degraded request), so the extra run is a deliberate
		// simplicity-over-throughput trade — cheaper than widening the IEnrolmentEvaluator surface to return
		// the ejected set from the failed ValidatedEvaluation. Revisit only if this path is ever shown hot.
		var ejected = evaluation.Value is null ? engine.StaleChoices(input, cancellationToken) : [];
		return TypedResults.Ok(EnrolmentEvaluateResponseFactory.Create(evaluation, ejected));
	}
}
