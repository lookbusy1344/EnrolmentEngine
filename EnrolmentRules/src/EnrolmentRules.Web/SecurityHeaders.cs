namespace EnrolmentRules.Web;

/// <summary>Baseline hardening headers applied to every response, not just the API (F15).</summary>
public static class SecurityHeaders
{
	/// <summary>
	///     <c>_Layout.cshtml</c> loads Bootstrap and Google Fonts and no other third-party origin, and the
	///     app ships no inline <c>&lt;script&gt;</c>; Bootstrap's own JS (dropdowns, offcanvas) sets element
	///     style properties directly rather than through a <c>style</c> attribute string, but is allowed
	///     inline styling anyway since that distinction is not worth chasing browser-by-browser.
	/// </summary>
	private const string ContentSecurityPolicy =
		"default-src 'self'; "
		+ "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
		+ "font-src 'self' https://fonts.gstatic.com; "
		+ "img-src 'self' data:; "
		+ "script-src 'self'; "
		+ "connect-src 'self'; "
		+ "base-uri 'self'; "
		+ "frame-ancestors 'none'";

	public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app.Use(async (context, next) => {
			context.Response.Headers.XContentTypeOptions = "nosniff";
			context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
			context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
			await next(context);
		});
	}
}
