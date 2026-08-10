namespace EnrolmentRules.Web.Tests;

using Microsoft.AspNetCore.Mvc.Testing;

/// <summary>
///     Hosts the real <c>Program</c> app over the shipped <c>workflows/</c> and <c>data/</c> directories, so
///     integration tests exercise the same DI wiring and rule set as production rather than a test double.
/// </summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
	public static string RepoRoot { get; } = FindRepoRoot();

	// ContentRoot points at the repo root so workflows/ and data/ resolve without a build-output
	// copy step; wwwroot lives one level down under the project folder instead, so WebRoot is
	// pointed there explicitly rather than defaulting to (the nonexistent) {RepoRoot}/wwwroot.
	protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
		.UseContentRoot(RepoRoot)
		.UseWebRoot(Path.Combine(RepoRoot, "src", "EnrolmentRules.Web", "wwwroot"));

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
