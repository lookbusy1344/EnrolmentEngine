namespace EnrolmentRules.Web.Services;

using Microsoft.AspNetCore.Http;

/// <summary>
///     The last policy id a request resolved to, carried the same way <see cref="IEnrolmentStateStore" />
///     carries the facts snapshot — a small, self-contained cookie, not server-side state.
/// </summary>
public static class EnrolmentPolicyCookie
{
	private const string CookieName = "enrolment.policy";

	public static string? Read(HttpContext context) =>
		context.Request.Cookies.TryGetValue(CookieName, out var value) ? value : null;

	public static void Write(HttpContext context, string policyId) =>
		context.Response.Cookies.Append(
			CookieName,
			policyId,
			new() {
				Path = "/razor",
				SameSite = SameSiteMode.Lax,
				HttpOnly = true,
				Secure = context.Request.IsHttps,
			});
}
