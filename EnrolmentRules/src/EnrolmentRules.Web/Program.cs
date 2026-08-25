namespace EnrolmentRules.Web;

using Api;
using Engine.Hosting;
using Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Services;

/// <summary>Entry point, exposed as a named class so <c>WebApplicationFactory&lt;Program&gt;</c> can host this app for integration tests.</summary>
public sealed class Program
{
	private Program() { }

	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		_ = builder.Services.ConfigureHttpJsonOptions(options =>
			options.SerializerOptions.TypeInfoResolverChain.Insert(0, EnrolmentApiJsonContext.Default));
		// "/App" also serves "" ("/") so the Vue app is the sole front end with no redirect hop.
		_ = builder.Services.AddRazorPages(options => options.Conventions.AddPageRoute("/App", ""));
		_ = builder.Services.AddSingleton(TimeProvider.System);
		_ = builder.Services.AddSingleton<IViteManifestReader, ViteManifestReader>();
		_ = builder.Services.AddEnrolmentPolicies(options => options
															 .UseDefault(
																 "standard",
																 "Standard",
																 new DirectoryDataSource(
																	 Path.Combine(builder.Environment.ContentRootPath, "workflows"),
																	 Path.Combine(builder.Environment.ContentRootPath, "data")))
															 .Add(
																 "elite",
																 "Elite",
																 new OverlayEnrolmentDataSource(
																	 new DirectoryDataSource(
																		 Path.Combine(builder.Environment.ContentRootPath, "policies", "elite", "workflows"),
																		 Path.Combine(builder.Environment.ContentRootPath, "policies", "elite", "data")),
																	 new DirectoryDataSource(
																		 Path.Combine(builder.Environment.ContentRootPath, "workflows"),
																		 Path.Combine(builder.Environment.ContentRootPath, "data"))))
															 .UseTimeProvider());

		var app = builder.Build();

		if (!app.Environment.IsDevelopment()) {
			_ = app.UseExceptionHandler(exceptionApp => exceptionApp.Run(HandleUnhandledExceptionAsync));
		}

		_ = app.UseStaticFiles();
		_ = app.UseRouting();
		_ = app.UseEnrolmentEvaluateRequestSizeLimit();
		_ = app.MapRazorPages();
		_ = app.MapEnrolmentApi();

		app.Run();
	}

	// One error contract for both page and API requests: a stable 500 with no exception detail leaked
	// (message, stack trace, inner exception), so a runtime failure — however it happened — never
	// re-executes a route or serves the framework's default HTML error page. Self-contained: it reads
	// only the request path and writes the response, so it cannot itself throw when the failure that
	// triggered it was, say, a missing Vite manifest or a malformed state cookie.
	private static async Task HandleUnhandledExceptionAsync(HttpContext context)
	{
		context.Response.StatusCode = StatusCodes.Status500InternalServerError;
		context.Response.Headers.CacheControl = "no-store";

		if (context.Request.Path.StartsWithSegments("/api")) {
			// contentType must be passed explicitly — WriteAsJsonAsync otherwise defaults it to "application/json".
			await context.Response.WriteAsJsonAsync(
				new ProblemDetails {
					Status = StatusCodes.Status500InternalServerError,
					Title = "An unexpected error occurred.",
				},
				options: null,
				contentType: "application/problem+json");
			return;
		}

		context.Response.ContentType = "text/plain";
		await context.Response.WriteAsync("An unexpected error occurred.");
	}
}
