namespace EnrolmentRules.Engine;

using Domain;
using Prediction;

/// <summary>
///     Filesystem-backed enrolment data source matching the shipped layout: workflows under one
///     directory and the data files beneath the sibling <c>data/</c> directory.
/// </summary>
public sealed class DirectoryDataSource(string workflowsDirectory, string dataDirectory) : IEnrolmentDataSource
{
	public IReadOnlyList<(string FileName, Stream Content)> OpenWorkflows() =>
		OpenWorkflowFiles(
			Directory.EnumerateFiles(workflowsDirectory)
				.Where(IsWorkflowFile)
				.OrderBy(static file => file, StringComparer.Ordinal),
			File.OpenRead);

	public Stream OpenWorkflowSchema() => File.OpenRead(Path.Combine(workflowsDirectory, WorkflowStore.SchemaFileName));

	public Stream OpenCatalogue() => File.OpenRead(Path.Combine(dataDirectory, CatalogueStore.CatalogueFileName));

	public Stream OpenCatalogueSchema() => File.OpenRead(Path.Combine(dataDirectory, CatalogueStore.SchemaFileName));

	public Stream OpenQualifications() => File.OpenRead(Path.Combine(dataDirectory, QualificationScaleStore.QualificationsFileName));

	public Stream OpenQualificationsSchema() => File.OpenRead(Path.Combine(dataDirectory, QualificationScaleStore.SchemaFileName));

	public Stream OpenThresholds() => File.OpenRead(Path.Combine(dataDirectory, PolicyThresholdsStore.ThresholdsFileName));

	public Stream OpenThresholdsSchema() => File.OpenRead(Path.Combine(dataDirectory, PolicyThresholdsStore.SchemaFileName));

	public Stream OpenTransitionMatrix() =>
		File.OpenRead(Path.Combine(dataDirectory, DfeTransitionMatrix.DataDirectoryRelativePath));

	internal static IReadOnlyList<(string FileName, Stream Content)> OpenWorkflowFiles(
		IEnumerable<string> files,
		Func<string, Stream> openFile)
	{
		var opened = new List<(string FileName, Stream Content)>();
		try {
			foreach (var file in files) {
				opened.Add((file, openFile(file)));
			}

			return opened;
		}
		catch {
			foreach (var (_, content) in opened) {
				content.Dispose();
			}

			throw;
		}
	}

	private static bool IsWorkflowFile(string file) =>
		!string.Equals(Path.GetFileName(file), WorkflowStore.SchemaFileName, StringComparison.OrdinalIgnoreCase)
		&& Path.GetExtension(file) is ".json" or ".yaml" or ".yml";
}
