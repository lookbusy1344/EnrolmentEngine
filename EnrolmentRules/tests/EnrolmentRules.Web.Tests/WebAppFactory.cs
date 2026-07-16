namespace EnrolmentRules.Web.Tests;

using Microsoft.AspNetCore.Mvc.Testing;

/// <summary>
///     Hosts the real <c>Program</c> app over the shipped <c>workflows/</c> and <c>data/</c> directories, so
///     integration tests exercise the same DI wiring and rule set as production rather than a test double.
/// </summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
	public static string RepoRoot { get; } = FindRepoRoot();

	protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseContentRoot(RepoRoot);

	private static string FindRepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			if (File.Exists(Path.Combine(dir.FullName, "EnrolmentRules.slnx"))) {
				return dir.FullName;
			}

			dir = dir.Parent;
		}

		throw new InvalidOperationException("Could not locate repo root (EnrolmentRules.slnx) from " + AppContext.BaseDirectory);
	}
}
