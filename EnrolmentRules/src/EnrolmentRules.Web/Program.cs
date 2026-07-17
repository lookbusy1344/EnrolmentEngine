namespace EnrolmentRules.Web;

using Api;
using Configuration;
using Extensions.DependencyInjection;
using Services;

/// <summary>Entry point, exposed as a named class so <c>WebApplicationFactory&lt;Program&gt;</c> can host this app for integration tests.</summary>
public sealed class Program
{
	private Program()
	{
	}

	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		// Registered as a factory, not an eagerly-computed instance: WebApplicationFactory-driven tests
		// mutate IConfiguration via a hook that only takes effect once WebApplicationBuilder.Build()
		// runs, so validating EnrolmentWeb:DefaultExperience here (between CreateBuilder and Build) would
		// see stale/default configuration under test. Forcing resolution once, right after Build() below,
		// keeps the fail-fast startup behaviour while still observing test overrides.
		_ = builder.Services.AddSingleton(sp => EnrolmentWebOptions.LoadAndValidate(sp.GetRequiredService<IConfiguration>()));
		_ = builder.Services.ConfigureHttpJsonOptions(options =>
			options.SerializerOptions.TypeInfoResolverChain.Insert(0, EnrolmentApiJsonContext.Default));
		_ = builder.Services.AddRazorPages();
		_ = builder.Services.AddDistributedMemoryCache();
		_ = builder.Services.AddSession();
		_ = builder.Services.AddSingleton(TimeProvider.System);
		_ = builder.Services.AddSingleton<IEnrolmentSessionStore, EnrolmentSessionStore>();
		_ = builder.Services.AddScoped<EnrolmentOptionsService>();
		_ = builder.Services.AddSingleton<IViteManifestReader, ViteManifestReader>();
		_ = builder.Services.AddEnrolmentEngine(options => options
			.UseWorkflowsDirectory(Path.Combine(builder.Environment.ContentRootPath, "workflows"))
			.UseDataDirectory(Path.Combine(builder.Environment.ContentRootPath, "data"))
			.UseTimeProvider());

		var app = builder.Build();

		// Force eager resolution so a bad EnrolmentWeb:DefaultExperience fails startup, not the first request.
		_ = app.Services.GetRequiredService<EnrolmentWebOptions>();

		if (!app.Environment.IsDevelopment()) {
			_ = app.UseExceptionHandler("/Error");
		}

		_ = app.UseStaticFiles();
		_ = app.UseRouting();
		_ = app.UseSession();
		_ = app.MapRazorPages();
		_ = app.MapEnrolmentApi();

		app.Run();
	}
}
