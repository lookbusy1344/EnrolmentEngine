namespace EnrolmentRules.Tests;

using System.Text.Json;
using Domain;
using Engine;
using FluentAssertions;
using Prediction;

/// <summary>
///     Phase 2 bootstrap coverage — prove the engine can be assembled from streams, and that the new
///     data-source abstraction matches the directory-based startup path.
/// </summary>
public sealed class BootstrapTests
{
	private static string DataDir => Harness.DataDir;

	[Fact]
	public void dfe_transition_matrix_loads_equally_from_a_stream_and_a_path()
	{
		var path = Path.Combine(DataDir, DfeTransitionMatrix.DataDirectoryRelativePath);
		var expected = DfeTransitionMatrix.Load(path);

		using var stream = File.OpenRead(path);
		var actual = DfeTransitionMatrix.Load(stream);

		actual.EvidenceFor(6.5, Harness.Catalogue).Should()
			.BeEquivalentTo(expected.EvidenceFor(6.5, Harness.Catalogue));
	}

	[Fact]
	public async Task create_async_does_not_mutate_the_process_global_scale()
	{
		// The factory threads the loaded scale explicitly through the engine, so it must not also clobber
		// the process-global table — otherwise two engines built with different scales race in one process.
		var before = QualificationScale.Current;
		_ = await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, DataDir, Harness.AsOf);
		QualificationScale.Current.Should().BeSameAs(before);
	}

	[Fact]
	public async Task directory_data_source_matches_the_directory_bootstrap_path()
	{
		var expected = await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, DataDir, Harness.AsOf);
		var actual = await EnrolmentEngine.CreateAsync(new DirectoryDataSource(Harness.WorkflowsDir, DataDir), Harness.AsOf);

		var student = ExampleStudent();
		(await actual.EvaluateAsync(student)).Should().BeEquivalentTo(await expected.EvaluateAsync(student));
	}

	[Fact]
	public async Task in_memory_data_source_bootstraps_the_engine_without_files_on_disk()
	{
		var source = InMemoryDataSource.FromRepositoryLayout(Harness.WorkflowsDir, DataDir);
		var created = await EnrolmentEngine.CreateAsync(source, Harness.AsOf);
		var expected = await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, DataDir, Harness.AsOf);

		var student = ExampleStudent();
		(await created.EvaluateAsync(student)).Should().BeEquivalentTo(await expected.EvaluateAsync(student));
	}

	private static StudentInput ExampleStudent()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "student.json");
		var json = File.ReadAllText(path);
		var document = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentDocument);
		return document!.Student;
	}

	private static bool IsWorkflowFile(string file) =>
		!string.Equals(Path.GetFileName(file), WorkflowStore.SchemaFileName, StringComparison.OrdinalIgnoreCase)
		&& Path.GetExtension(file) is ".json" or ".yaml" or ".yml";

	private sealed class InMemoryDataSource : IEnrolmentDataSource
	{
		private readonly byte[] catalogue;
		private readonly byte[] catalogueSchema;
		private readonly byte[] qualifications;
		private readonly byte[] qualificationsSchema;
		private readonly byte[] thresholds;
		private readonly byte[] thresholdsSchema;
		private readonly byte[] transitionMatrix;
		private readonly IReadOnlyList<(string FileName, byte[] Bytes)> workflows;
		private readonly byte[] workflowSchema;

		private InMemoryDataSource(
			IReadOnlyList<(string FileName, byte[] Bytes)> workflows,
			byte[] workflowSchema,
			byte[] catalogue,
			byte[] catalogueSchema,
			byte[] qualifications,
			byte[] qualificationsSchema,
			byte[] thresholds,
			byte[] thresholdsSchema,
			byte[] transitionMatrix)
		{
			this.workflows = workflows;
			this.workflowSchema = workflowSchema;
			this.catalogue = catalogue;
			this.catalogueSchema = catalogueSchema;
			this.qualifications = qualifications;
			this.qualificationsSchema = qualificationsSchema;
			this.thresholds = thresholds;
			this.thresholdsSchema = thresholdsSchema;
			this.transitionMatrix = transitionMatrix;
		}

		public IReadOnlyList<(string FileName, Stream Content)> OpenWorkflows() =>
			[.. workflows.Select(static workflow => (workflow.FileName, (Stream)new MemoryStream(workflow.Bytes, false)))];

		public Stream OpenWorkflowSchema() => new MemoryStream(workflowSchema, false);

		public Stream OpenCatalogue() => new MemoryStream(catalogue, false);

		public Stream OpenCatalogueSchema() => new MemoryStream(catalogueSchema, false);

		public Stream OpenQualifications() => new MemoryStream(qualifications, false);

		public Stream OpenQualificationsSchema() => new MemoryStream(qualificationsSchema, false);

		public Stream OpenThresholds() => new MemoryStream(thresholds, false);

		public Stream OpenThresholdsSchema() => new MemoryStream(thresholdsSchema, false);

		public Stream OpenTransitionMatrix() => new MemoryStream(transitionMatrix, false);

		public static InMemoryDataSource FromRepositoryLayout(string workflowsDirectory, string dataDirectory)
		{
			var workflows = Directory.EnumerateFiles(workflowsDirectory)
				.Where(IsWorkflowFile)
				.OrderBy(static file => file, StringComparer.Ordinal)
				.Select(static file => (file, File.ReadAllBytes(file)))
				.ToArray();

			return new(
				workflows,
				File.ReadAllBytes(Path.Combine(workflowsDirectory, WorkflowStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, CatalogueStore.CatalogueFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, CatalogueStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, QualificationScaleStore.QualificationsFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, QualificationScaleStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, PolicyThresholdsStore.ThresholdsFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, PolicyThresholdsStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, DfeTransitionMatrix.DataDirectoryRelativePath)));
		}
	}
}
