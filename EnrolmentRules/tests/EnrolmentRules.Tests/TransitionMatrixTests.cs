namespace EnrolmentRules.Tests;

using System.Text;
using AwesomeAssertions;
using Cli;
using Domain;
using Engine;
using Prediction;

/// <summary>
///     Transition-matrix validation at the data boundary: malformed startup data must fail before the
///     engine or CLI publishes a runnable policy surface.
/// </summary>
public sealed class TransitionMatrixTests
{
	private const string Header =
		"subject,dfe_qualification_number,dfe_subject_number,dfe_subject_name,prior_attainment_band,probability_u,probability_e,probability_d,probability_c,probability_b,probability_a,probability_a_star";

	[Fact]
	public void loader_rejects_an_empty_stream()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(string.Empty));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*header row*");
	}

	[Fact]
	public void loader_rejects_a_header_only_stream()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(Header));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*at least one data row*");
	}

	[Fact]
	public void loader_rejects_a_wrong_header()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			"subject,wrong\n" +
			ValidRow()));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*header*");
	}

	[Fact]
	public void loader_rejects_the_wrong_field_count()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			"maths,111,12210,Mathematics,7 to < 8,0.1"));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*12 fields*");
	}

	[Fact]
	public void loader_rejects_a_malformed_numeric_value()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow(probabilityA: "nope")));

		var exception = act.Should().Throw<TransitionMatrixException>().Which;
		exception.Message.Should().Contain("probability_a");
		exception.InnerException.Should().BeOfType<FormatException>();
	}

	[Theory]
	[InlineData("NaN")]
	[InlineData("Infinity")]
	[InlineData("-Infinity")]
	public void loader_rejects_non_finite_probability_values(string value)
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow(probabilityA: value)));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*finite*");
	}

	[Theory]
	[InlineData("-0.1")]
	[InlineData("1.1")]
	public void loader_rejects_probability_values_outside_zero_to_one(string value)
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow(probabilityA: value)));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*[0, 1]*");
	}

	[Fact]
	public void loader_rejects_duplicate_subject_band_rows()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow() + "\n" +
			ValidRow()));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*duplicate*maths*7 to < 8*");
	}

	[Fact]
	public void loader_rejects_unknown_band_labels()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow("7+")));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*prior_attainment_band*7+*");
	}

	[Fact]
	public void loader_rejects_row_totals_that_do_not_sum_to_one()
	{
		var act = () => DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow(probabilityAStar: "0.1")));

		act.Should().Throw<TransitionMatrixException>()
			.WithMessage("*sum to 1*");
	}

	[Fact]
	public void sparse_low_band_falls_back_to_the_nearest_higher_populated_band()
	{
		var matrix = DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow("5 to < 6", "1.0", "0.0", "0.0", "0.0", "0.0", "0.0", "0.0")));

		var evidence = matrix.EvidenceFor(1.5, SingleSubjectCatalogue(Subject.Maths)).Single();

		evidence.PriorAttainmentBand.Should().Be("5 to < 6");
		evidence.ProbabilityU.Should().Be(1.0);
	}

	[Fact]
	public void sparse_high_band_still_falls_back_to_the_nearest_lower_populated_band()
	{
		var matrix = DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow("1 to < 2", "0.0", "1.0", "0.0", "0.0", "0.0", "0.0", "0.0")));

		var evidence = matrix.EvidenceFor(8.5, SingleSubjectCatalogue(Subject.Maths)).Single();

		evidence.PriorAttainmentBand.Should().Be("1 to < 2");
		evidence.ProbabilityE.Should().Be(1.0);
	}

	[Fact]
	public void equidistant_sparse_gap_prefers_the_lower_populated_band()
	{
		var matrix = DfeTransitionMatrix.Load(new StringReader(
			Header + "\n" +
			ValidRow("< 1", "0.0", "0.0", "1.0", "0.0", "0.0", "0.0", "0.0") + "\n" +
			ValidRow("2 to < 3", "0.0", "0.0", "0.0", "1.0", "0.0", "0.0", "0.0")));

		var evidence = matrix.EvidenceFor(1.5, SingleSubjectCatalogue(Subject.Maths)).Single();

		evidence.PriorAttainmentBand.Should().Be("< 1");
		evidence.ProbabilityD.Should().Be(1.0);
	}

	[Fact]
	public void create_rejects_a_syntactically_readable_but_invalid_transition_matrix()
	{
		var source = InMemoryDataSource.WithTransitionMatrix(
			Encoding.UTF8.GetBytes(Header + "\n" + ValidRow(probabilityA: "NaN")));

		var act = () => EnrolmentEngine.Create(source, Harness.AsOf);

		act.Should().Throw<TransitionMatrixException>();
	}

	[Fact]
	public async Task cli_reports_an_invalid_transition_matrix_as_an_input_error_for_evaluation_modes()
	{
		var path = WriteStudentJson();
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = CliRunner.Run(
			["--json", path],
			stdout,
			stderr,
			() => Harness.WorkflowsDir,
			InvalidDataDirectory);

		exit.Should().Be(CliRunner.ExitInput);
		stdout.ToString().Should().BeEmpty();
		stderr.ToString().Should().Contain("transition matrix");
	}

	[Fact]
	public async Task cli_reports_an_invalid_transition_matrix_as_an_input_error_for_profile_mode()
	{
		var path = WriteStudentJson();
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = CliRunner.Run(
			[path],
			stdout,
			stderr,
			() => Harness.WorkflowsDir,
			InvalidDataDirectory);

		exit.Should().Be(CliRunner.ExitInput);
		stdout.ToString().Should().BeEmpty();
		stderr.ToString().Should().Contain("transition matrix");
	}

	private static string ValidRow(
		string band = "7 to < 8",
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

	private static string WriteStudentJson()
	{
		var path = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N") + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, """
								{"student":{"id":"S-MATRIX","gcses":{"english_language":6,"maths":6,"physics":6,"chemistry":6,"biology":6},"hobbies":[],"date_of_birth":"2009-09-01"}}
								""");
		return path;
	}

	private static string InvalidDataDirectory()
	{
		var dataDir = CopyDirectory(Harness.DataDir);
		File.WriteAllText(
			Path.Combine(dataDir, DfeTransitionMatrix.DataDirectoryRelativePath),
			Header + "\n" + ValidRow(probabilityA: "NaN"));
		return dataDir;
	}

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

	private static CatalogueData SingleSubjectCatalogue(Subject subject) =>
		new(new Dictionary<Subject, SubjectMeta> { [subject] = Harness.Catalogue.Meta(subject) }, [subject]);

	private sealed class InMemoryDataSource(
		IReadOnlyList<(string FileName, byte[] Bytes)> workflows,
		byte[] workflowSchema,
		byte[] catalogue,
		byte[] catalogueSchema,
		byte[] qualifications,
		byte[] qualificationsSchema,
		byte[] thresholds,
		byte[] thresholdsSchema,
		byte[] transitionMatrix) : IEnrolmentDataSource
	{
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

		public static InMemoryDataSource WithTransitionMatrix(byte[] transitionMatrix)
		{
			var workflows = Directory.EnumerateFiles(Harness.WorkflowsDir)
				.Where(static file =>
					!string.Equals(Path.GetFileName(file), WorkflowStore.SchemaFileName, StringComparison.OrdinalIgnoreCase)
					&& Path.GetExtension(file) is ".json" or ".yaml" or ".yml")
				.OrderBy(static file => file, StringComparer.Ordinal)
				.Select(static file => (file, File.ReadAllBytes(file)))
				.ToArray();

			return new(
				workflows,
				File.ReadAllBytes(Path.Combine(Harness.WorkflowsDir, WorkflowStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(Harness.DataDir, CatalogueStore.CatalogueFileName)),
				File.ReadAllBytes(Path.Combine(Harness.DataDir, CatalogueStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(Harness.DataDir, QualificationScaleStore.QualificationsFileName)),
				File.ReadAllBytes(Path.Combine(Harness.DataDir, QualificationScaleStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(Harness.DataDir, PolicyThresholdsStore.ThresholdsFileName)),
				File.ReadAllBytes(Path.Combine(Harness.DataDir, PolicyThresholdsStore.SchemaFileName)),
				transitionMatrix);
		}
	}
}
