namespace EnrolmentRules.Domain;

/// <summary>
///     Locates a shipped file or directory: prefer the copy beside the executable (the publish output),
///     otherwise walk up from the working directory and the base directory to the first ancestor that holds
///     it.
/// </summary>
public static class ShippedLayout
{
	/// <summary>
	///     Locate <paramref name="relativePath" /> beside the executable, else in the nearest ancestor holding
	///     it. When <paramref name="rootMarker" /> is given, only an ancestor that also holds that marker file
	///     (the solution file) qualifies, pinning the walk to the repository root when running from the source
	///     tree rather than a published output.
	/// </summary>
	public static string Locate(string relativePath, string? rootMarker = null)
	{
		var bundled = Path.Combine(AppContext.BaseDirectory, relativePath);
		if (Path.Exists(bundled)) {
			return bundled;
		}

		string[] starts = [Directory.GetCurrentDirectory(), AppContext.BaseDirectory];
		foreach (var start in starts) {
			for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent) {
				if (rootMarker is not null && !File.Exists(Path.Combine(dir.FullName, rootMarker))) {
					continue;
				}

				var candidate = Path.Combine(dir.FullName, relativePath);
				if (Path.Exists(candidate)) {
					return candidate;
				}
			}
		}

		throw new FileNotFoundException($"Could not locate '{relativePath}'.");
	}
}
