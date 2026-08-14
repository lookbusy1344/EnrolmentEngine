namespace EnrolmentRules.Tests;

using AwesomeAssertions;

/// <summary>
///     Elite auxiliary policy plan, step 2.2 — <see cref="OverlayEnrolmentDataSource" /> layers an
///     auxiliary policy's own workflows/catalogue/thresholds over a shared base source for the schemas,
///     the qualification scale and the DfE transition matrix. Proves the routing, that every open returns
///     a fresh stream, and that the auxiliary tree needs none of the delegated assets.
/// </summary>
public sealed class OverlayEnrolmentDataSourceTests
{
	private static string AuxiliaryDirectory()
	{
		var dir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "overlay-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	[Fact]
	public void workflows_catalogue_and_thresholds_come_from_the_auxiliary_source()
	{
		var auxWorkflows = AuxiliaryDirectory();
		var auxData = AuxiliaryDirectory();
		File.WriteAllText(Path.Combine(auxWorkflows, "eligibility.yaml"), "WorkflowName: 'eligibility'\nRules: []\n");
		File.WriteAllText(Path.Combine(auxData, "catalogue.yaml"), "aux-catalogue-marker");
		File.WriteAllText(Path.Combine(auxData, "thresholds.yaml"), "aux-thresholds-marker");

		var auxiliary = new DirectoryDataSource(auxWorkflows, auxData);
		var overlay = new OverlayEnrolmentDataSource(auxiliary, new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir));

		var workflows = overlay.OpenWorkflows();
		try {
			workflows.Should().ContainSingle(w => w.FileName.EndsWith("eligibility.yaml", StringComparison.Ordinal));
		}
		finally {
			foreach (var workflow in workflows) {
				workflow.Dispose();
			}
		}

		using var catalogueReader = new StreamReader(overlay.OpenCatalogue());
		catalogueReader.ReadToEnd().Should().Be("aux-catalogue-marker");

		using var thresholdsReader = new StreamReader(overlay.OpenThresholds());
		thresholdsReader.ReadToEnd().Should().Be("aux-thresholds-marker");
	}

	[Fact]
	public void schemas_qualifications_and_the_transition_matrix_come_from_the_base_source()
	{
		// The auxiliary tree carries none of these files at all — if the overlay ever routed to it for
		// them, this would throw FileNotFoundException instead of succeeding.
		var auxWorkflows = AuxiliaryDirectory();
		var auxData = AuxiliaryDirectory();
		var @base = new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir);
		var overlay = new OverlayEnrolmentDataSource(new DirectoryDataSource(auxWorkflows, auxData), @base);

		using var overlaySchema = new StreamReader(overlay.OpenWorkflowSchema());
		using var baseSchema = new StreamReader(@base.OpenWorkflowSchema());
		overlaySchema.ReadToEnd().Should().Be(baseSchema.ReadToEnd());

		using var overlayCatalogueSchema = new StreamReader(overlay.OpenCatalogueSchema());
		using var baseCatalogueSchema = new StreamReader(@base.OpenCatalogueSchema());
		overlayCatalogueSchema.ReadToEnd().Should().Be(baseCatalogueSchema.ReadToEnd());

		using var overlayQualifications = new StreamReader(overlay.OpenQualifications());
		using var baseQualifications = new StreamReader(@base.OpenQualifications());
		overlayQualifications.ReadToEnd().Should().Be(baseQualifications.ReadToEnd());

		using var overlayQualificationsSchema = new StreamReader(overlay.OpenQualificationsSchema());
		using var baseQualificationsSchema = new StreamReader(@base.OpenQualificationsSchema());
		overlayQualificationsSchema.ReadToEnd().Should().Be(baseQualificationsSchema.ReadToEnd());

		using var overlayThresholdsSchema = new StreamReader(overlay.OpenThresholdsSchema());
		using var baseThresholdsSchema = new StreamReader(@base.OpenThresholdsSchema());
		overlayThresholdsSchema.ReadToEnd().Should().Be(baseThresholdsSchema.ReadToEnd());

		using var overlayMatrix = new StreamReader(overlay.OpenTransitionMatrix());
		using var baseMatrix = new StreamReader(@base.OpenTransitionMatrix());
		overlayMatrix.ReadToEnd().Should().Be(baseMatrix.ReadToEnd());
	}

	[Fact]
	public void every_open_returns_a_fresh_stream()
	{
		var overlay = new OverlayEnrolmentDataSource(
			new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir),
			new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir));

		using var first = overlay.OpenCatalogue();
		using var second = overlay.OpenCatalogue();

		first.Should().NotBeSameAs(second);
	}

	[Fact]
	public void an_auxiliary_open_failure_propagates_the_directory_source_exception_type_unchanged()
	{
		var missingWorkflows = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "missing-" + Guid.NewGuid().ToString("N"));
		var overlay = new OverlayEnrolmentDataSource(
			new DirectoryDataSource(missingWorkflows, AuxiliaryDirectory()),
			new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir));

		var act = overlay.OpenWorkflows;

		act.Should().Throw<DirectoryNotFoundException>();
	}
}
