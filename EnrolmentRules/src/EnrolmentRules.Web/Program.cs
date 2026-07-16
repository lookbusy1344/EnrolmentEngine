namespace EnrolmentRules.Web;

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

		_ = builder.Services.AddRazorPages();
		_ = builder.Services.AddDistributedMemoryCache();
		_ = builder.Services.AddSession();
		_ = builder.Services.AddSingleton(TimeProvider.System);
		_ = builder.Services.AddSingleton<IEnrolmentSessionStore, EnrolmentSessionStore>();
		_ = builder.Services.AddEnrolmentEngine(options => options
			.UseWorkflowsDirectory(Path.Combine(builder.Environment.ContentRootPath, "workflows"))
			.UseDataDirectory(Path.Combine(builder.Environment.ContentRootPath, "data"))
			.UseTimeProvider());

		var app = builder.Build();

		if (!app.Environment.IsDevelopment()) {
			_ = app.UseExceptionHandler("/Error");
		}

		_ = app.UseStaticFiles();
		_ = app.UseRouting();
		_ = app.UseSession();
		_ = app.MapRazorPages();

		app.Run();
	}
}
