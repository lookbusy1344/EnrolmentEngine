namespace EnrolmentRules.Web.Api;

using Engine;
using Microsoft.AspNetCore.Http.HttpResults;
using Services;

/// <summary>Maps the stateless <c>/api/enrolment/*</c> endpoints the Vue client calls; no session, no cookies.</summary>
public static class EnrolmentApiEndpoints
{
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
		if (!EnrolmentApiMapper.TryToStudentInput(request, out var input)) {
			return TypedResults.BadRequest(
				"Could not map the posted snapshot: an unrecognised prior-qualification type or chosen A-level subject value.");
		}

		var evaluation = engine.TryExplain(input, cancellationToken);

		// On the rejection path TryExplain has already found the stale choices internally but discards them, so
		// StaleChoices re-runs the pipeline once to recover the subjects to eject. The cost lands only when a
		// committed choice has gone red (a rare, already-degraded request), so the extra run is a deliberate
		// simplicity-over-throughput trade — cheaper than widening the IEnrolmentEvaluator surface to return
		// the ejected set from the failed ValidatedEvaluation. Revisit only if this path is ever shown hot.
		var ejected = evaluation.Value is null ? engine.StaleChoices(input, cancellationToken) : [];
		return TypedResults.Ok(EnrolmentEvaluateResponseFactory.Create(evaluation, ejected));
	}
}
