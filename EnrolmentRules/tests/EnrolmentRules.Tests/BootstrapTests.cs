namespace EnrolmentRules.Tests;

using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Domain;
using Prediction;

/// <summary>
///     Phase 2 bootstrap coverage — prove the engine can be assembled from streams, and that the new
///     data-source abstraction matches the directory-based startup path.
/// </summary>
public sealed class BootstrapTests
{
	private const string TransitionHeader =
		"subject,dfe_qualification_number,dfe_subject_number,dfe_subject_name,prior_attainment_band,probability_u,probability_e,probability_d,probability_c,probability_b,probability_a,probability_a_star";

	private static string DataDir => Harness.DataDir;

	[Fact]
	public void transition_matrix_stream_and_path_loaders_match_for_a_tiny_known_fixture()
	{
		var directory = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, "transition.csv");
		File.WriteAllText(path, string.Join(
			Environment.NewLine,
			TransitionHeader,
			ValidTransitionRow("< 1", "0.0", "1.0", "0.0", "0.0", "0.0", "0.0", "0.0"),
			ValidTransitionRow("7 to < 8")));

		var fromPath = DfeTransitionMatrix.Load(path);

		using var stream = File.OpenRead(path);
		var fromStream = DfeTransitionMatrix.Load(stream);

		var lowBandPath = fromPath.EvidenceFor(0.5, SingleSubjectCatalogue(Subject.Maths)).Single();
		var highBandStream = fromStream.EvidenceFor(7.5, SingleSubjectCatalogue(Subject.Maths)).Single();

		lowBandPath.PriorAttainmentBand.Should().Be("< 1");
		lowBandPath.ProbabilityE.Should().Be(1.0);
		highBandStream.PriorAttainmentBand.Should().Be("7 to < 8");
		highBandStream.ProbabilityA.Should().Be(0.2);

		fromStream.EvidenceFor(7.5, SingleSubjectCatalogue(Subject.Maths)).Should()
			.BeEquivalentTo(fromPath.EvidenceFor(7.5, SingleSubjectCatalogue(Subject.Maths)));
	}

	[Fact]
	public void create_threads_its_own_scale_and_leaves_the_shipped_default_untouched()
	{
		// The factory threads the loaded scale explicitly through the engine. The shipped default is an
		// immutable snapshot with no installer, so building an engine cannot swap it — the same instance is
		// observed before and after, and the engine's own scale is what it loaded, not the ambient default.
		var before = QualificationScale.Default;
		var created = EnrolmentEngine.Create(Harness.WorkflowsDir, DataDir, Harness.AsOf);

		QualificationScale.Default.Should().BeSameAs(before);
		created.Scale.Should().NotBeNull();
	}

	[Fact]
	public void directory_data_source_matches_the_directory_bootstrap_path_for_the_same_student()
	{
		var expected = EnrolmentEngine.Create(Harness.WorkflowsDir, DataDir, Harness.AsOf);
		var actual = EnrolmentEngine.Create(new DirectoryDataSource(Harness.WorkflowsDir, DataDir), Harness.AsOf);

		var student = GoldenStudent("top-allrounder");
		actual.Evaluate(student).Should().BeEquivalentTo(expected.Evaluate(student));
	}

	[Fact]
	public void in_memory_data_source_matches_the_directory_bootstrap_path_for_the_same_student()
	{
		var source = InMemoryDataSource.FromRepositoryLayout(Harness.WorkflowsDir, DataDir);
		var created = EnrolmentEngine.Create(source, Harness.AsOf);
		var expected = EnrolmentEngine.Create(Harness.WorkflowsDir, DataDir, Harness.AsOf);

		var student = GoldenStudent("top-allrounder");
		created.Evaluate(student).Should().BeEquivalentTo(expected.Evaluate(student));
	}

	[Fact]
	public void directory_data_source_bootstraps_the_expected_catalogue_scale_and_student_contracts()
	{
		var created = EnrolmentEngine.Create(new DirectoryDataSource(Harness.WorkflowsDir, DataDir), Harness.AsOf);

		AssertBootstrapContract(created);
	}

	[Fact]
	public void in_memory_data_source_bootstraps_the_expected_catalogue_scale_and_student_contracts()
	{
		var created = EnrolmentEngine.Create(InMemoryDataSource.FromRepositoryLayout(Harness.WorkflowsDir, DataDir), Harness.AsOf);

		AssertBootstrapContract(created);
	}

	[Fact]
	public void directory_data_source_reports_a_missing_catalogue_file()
	{
		var dataDir = CopyDirectory(DataDir);
		File.Delete(Path.Combine(dataDir, CatalogueStore.CatalogueFileName));

		var act = () => EnrolmentEngine.Create(new DirectoryDataSource(Harness.WorkflowsDir, dataDir), Harness.AsOf);

		act.Should().Throw<FileNotFoundException>();
	}

	[Fact]
	public void in_memory_data_source_reports_an_invalid_transition_matrix()
	{
		var source = InMemoryDataSource.FromRepositoryLayout(
			Harness.WorkflowsDir,
			DataDir,
			Encoding.UTF8.GetBytes(TransitionHeader + Environment.NewLine + ValidTransitionRow("7 to < 8", probabilityA: "NaN")));

		var act = () => EnrolmentEngine.Create(source, Harness.AsOf);

		act.Should().Throw<TransitionMatrixException>();
	}

	private static void AssertBootstrapContract(EnrolmentEngine engine)
	{
		engine.Catalogue.Subjects.Should().Equal(Harness.Catalogue.Subjects);
		engine.Scale.Equivalence(QualificationType.BtecDiploma, "distinction").Should().Be(ALevelGrade.A);

		var result = engine.Evaluate(GoldenStudent("top-allrounder"));
		result.Eligible.Should().BeTrue();
		result.Recommendations.Single(recommendation => recommendation.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Red);
		result.Adjustments.Should().BeEmpty();
	}

	private static StudentInput GoldenStudent(string fixture)
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "golden", fixture + ".json");
		var json = File.ReadAllText(path);
		var document = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentDocument);
		return document!.Student;
	}

	private static CatalogueData SingleSubjectCatalogue(Subject subject) =>
		new(new Dictionary<Subject, SubjectMeta> { [subject] = Harness.Catalogue.Meta(subject) }, [subject]);

	private static string ValidTransitionRow(
		string band,
		string probabilityU = "0.1",
		string probabilityE = "0.1",
		string probabilityD = "0.1",
		string probabilityC = "0.1",
		string probabilityB = "0.1",
		string probabilityA = "0.2",
		string probabilityAStar = "0.3") =>
		string.Join(',',
			"maths",
			"111",
			"12210",
			"Mathematics",
			band,
			probabilityU,
			probabilityE,
			probabilityD,
			probabilityC,
			probabilityB,
			probabilityA,
			probabilityAStar);

	private static string CopyDirectory(string source)
	{
		var destination = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"), "payload");
		foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) {
			Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
		}

		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), true);
		}

		return destination;
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

		public IReadOnlyList<WorkflowContent> OpenWorkflows() =>
			[.. workflows.Select(static workflow => new WorkflowContent(workflow.FileName, new MemoryStream(workflow.Bytes, false)))];

		public Stream OpenWorkflowSchema() => new MemoryStream(workflowSchema, false);

		public Stream OpenCatalogue() => new MemoryStream(catalogue, false);

		public Stream OpenCatalogueSchema() => new MemoryStream(catalogueSchema, false);

		public Stream OpenQualifications() => new MemoryStream(qualifications, false);

		public Stream OpenQualificationsSchema() => new MemoryStream(qualificationsSchema, false);

		public Stream OpenThresholds() => new MemoryStream(thresholds, false);

		public Stream OpenThresholdsSchema() => new MemoryStream(thresholdsSchema, false);

		public Stream OpenTransitionMatrix() => new MemoryStream(transitionMatrix, false);

		public static InMemoryDataSource FromRepositoryLayout(
			string workflowsDirectory,
			string dataDirectory,
			byte[]? transitionMatrix = null)
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
				transitionMatrix ?? File.ReadAllBytes(Path.Combine(dataDirectory, DfeTransitionMatrix.DataDirectoryRelativePath)));
		}
	}
}
