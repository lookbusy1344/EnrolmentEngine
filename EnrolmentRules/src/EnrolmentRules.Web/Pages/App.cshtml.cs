namespace EnrolmentRules.Web.Pages;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Services;

public sealed class AppModel(IViteManifestReader manifestReader) : PageModel
{
	private const string EntrySourcePath = "src/main.ts";

	public ViteAssetPaths Assets { get; private set; } = null!;

	public void OnGet() => Assets = manifestReader.GetEntryAssets(EntrySourcePath);
}
