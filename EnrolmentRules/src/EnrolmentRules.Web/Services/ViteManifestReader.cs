namespace EnrolmentRules.Web.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using Infrastructure;

/// <summary>One resolved Vite build entry: the hashed script the shell must load, and any hashed stylesheets it pulled in.</summary>
public sealed record ViteAssetPaths(string ScriptPath, EquatableArray<string> StylesheetPaths);

/// <summary>
///     Resolves a Vite <c>manifest.json</c> entry to the request-servable asset paths under <c>/app</c>, so
///     <c>App.cshtml</c> never hard-codes a content hash that changes on every frontend build.
/// </summary>
public interface IViteManifestReader
{
	/// <summary>Resolves the built script/stylesheet paths for the Vite entry named by <paramref name="entrySourcePath" /> (e.g. <c>"src/main.ts"</c>).</summary>
	/// <exception cref="InvalidOperationException">The manifest is missing, unreadable, or has no entry for <paramref name="entrySourcePath" />.</exception>
	ViteAssetPaths GetEntryAssets(string entrySourcePath);
}

/// <summary>
///     Reads <c>wwwroot/app/manifest.json</c>, the file <c>vite build</c> writes there per
///     <c>vite.config.ts</c>'s <c>build.manifest: 'manifest.json'</c> — deliberately not Vite's
///     default <c>.vite/manifest.json</c>, which MSBuild's default Content glob (dot-directories
///     excluded) would silently drop from both build output and the publish payload.
/// </summary>
public sealed class ViteManifestReader(IWebHostEnvironment environment) : IViteManifestReader
{
	private const string AppBasePath = "/app/";

	public ViteAssetPaths GetEntryAssets(string entrySourcePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entrySourcePath);

		var manifestPath = Path.Combine(environment.WebRootPath, "app", "manifest.json");
		Dictionary<string, ViteManifestEntry>? manifest;
		try {
			using var stream = File.OpenRead(manifestPath);
			manifest = JsonSerializer.Deserialize(stream, ViteManifestJsonContext.Default.DictionaryStringViteManifestEntry);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			throw new InvalidOperationException(
				$"Could not read the Vite manifest at '{manifestPath}'. Run the ClientApp build before starting the app.", ex);
		}

		if (manifest is null || !manifest.TryGetValue(entrySourcePath, out var entry)) {
			throw new InvalidOperationException($"Vite manifest at '{manifestPath}' has no entry for '{entrySourcePath}'.");
		}

		return new(AppBasePath + entry.File, [.. entry.Css.Select(css => AppBasePath + css)]);
	}
}

internal sealed record ViteManifestEntry(string File, EquatableArray<string> Css);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, ViteManifestEntry>))]
[JsonSerializable(typeof(string))]
internal sealed partial class ViteManifestJsonContext : JsonSerializerContext;
