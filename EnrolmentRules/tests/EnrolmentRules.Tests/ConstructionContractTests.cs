namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     F3 — public construction and factory contracts fail fast, at the boundary, with the correct
///     exception and <c>ParamName</c>, rather than deferring a bad argument to an incidental
///     <see cref="NullReferenceException" /> during evaluation or startup.
/// </summary>
public sealed class ConstructionContractTests
{
	private static DirectoryDataSource ShippedSource() => new(Harness.WorkflowsDir, Harness.DataDir);

	// --- EnrolmentEngine.Create ---

	[Fact]
	public void create_rejects_a_null_data_source()
	{
		var act = static () => EnrolmentEngine.Create((IEnrolmentDataSource)null!, static () => Harness.AsOf);

		act.Should().Throw<ArgumentNullException>().WithParameterName("source");
	}

	[Fact]
	public void create_rejects_a_null_as_of_source()
	{
		var act = () => EnrolmentEngine.Create(ShippedSource(), (Func<DateOnly>)null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("asOf");
	}

	[Fact]
	public void directory_create_rejects_a_null_as_of_source()
	{
		var act = () => EnrolmentEngine.Create(Harness.WorkflowsDir, Harness.DataDir, (Func<DateOnly>)null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("asOf");
	}

	[Fact]
	public void create_validates_the_date_source_before_opening_policy_data()
	{
		var source = new CallTrackingDataSource();

		var act = () => EnrolmentEngine.Create(source, (Func<DateOnly>)null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("asOf");
		source.OpenCalls.Should().Be(0, "invalid arguments must be rejected before policy I/O starts");
	}

	[Fact]
	public void create_rejects_a_data_source_that_returns_a_null_workflow_list()
	{
		var act = () => EnrolmentEngine.Create(new NullReturningDataSource(nullWorkflows: true), static () => Harness.AsOf);

		act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(IEnrolmentDataSource.OpenWorkflows)}*");
	}

	[Theory]
	[InlineData(nameof(IEnrolmentDataSource.OpenWorkflowSchema))]
	[InlineData(nameof(IEnrolmentDataSource.OpenCatalogue))]
	[InlineData(nameof(IEnrolmentDataSource.OpenCatalogueSchema))]
	[InlineData(nameof(IEnrolmentDataSource.OpenQualifications))]
	[InlineData(nameof(IEnrolmentDataSource.OpenQualificationsSchema))]
	[InlineData(nameof(IEnrolmentDataSource.OpenThresholds))]
	[InlineData(nameof(IEnrolmentDataSource.OpenThresholdsSchema))]
	[InlineData(nameof(IEnrolmentDataSource.OpenTransitionMatrix))]
	public void create_rejects_a_data_source_that_returns_a_null_stream(string nullMember)
	{
		var act = () => EnrolmentEngine.Create(new NullReturningDataSource(nullStreamMember: nullMember), static () => Harness.AsOf);

		act.Should().Throw<InvalidOperationException>().WithMessage($"*{nullMember}*");
	}

	/// <summary>A conformance double: every member delegates to the real shipped source except the one under test.</summary>
	private sealed class NullReturningDataSource(bool nullWorkflows = false, string? nullStreamMember = null) : IEnrolmentDataSource
	{
		private readonly DirectoryDataSource inner = new(Harness.WorkflowsDir, Harness.DataDir);

		public IReadOnlyList<WorkflowContent> OpenWorkflows() => nullWorkflows ? null! : inner.OpenWorkflows();

		public Stream OpenWorkflowSchema() =>
			nullStreamMember == nameof(IEnrolmentDataSource.OpenWorkflowSchema) ? null! : inner.OpenWorkflowSchema();

		public Stream OpenCatalogue() => nullStreamMember == nameof(IEnrolmentDataSource.OpenCatalogue) ? null! : inner.OpenCatalogue();

		public Stream OpenCatalogueSchema() =>
			nullStreamMember == nameof(IEnrolmentDataSource.OpenCatalogueSchema) ? null! : inner.OpenCatalogueSchema();

		public Stream OpenQualifications() =>
			nullStreamMember == nameof(IEnrolmentDataSource.OpenQualifications) ? null! : inner.OpenQualifications();

		public Stream OpenQualificationsSchema() =>
			nullStreamMember == nameof(IEnrolmentDataSource.OpenQualificationsSchema) ? null! : inner.OpenQualificationsSchema();

		public Stream OpenThresholds() => nullStreamMember == nameof(IEnrolmentDataSource.OpenThresholds) ? null! : inner.OpenThresholds();

		public Stream OpenThresholdsSchema() =>
			nullStreamMember == nameof(IEnrolmentDataSource.OpenThresholdsSchema) ? null! : inner.OpenThresholdsSchema();

		public Stream OpenTransitionMatrix() =>
			nullStreamMember == nameof(IEnrolmentDataSource.OpenTransitionMatrix) ? null! : inner.OpenTransitionMatrix();
	}

	// --- EnrolmentEngineFactory.Create ---

	[Fact]
	public void factory_create_rejects_a_null_data_source()
	{
		var act = static () => EnrolmentEngineFactory.Create((IEnrolmentDataSource)null!, static () => Harness.AsOf);

		act.Should().Throw<ArgumentNullException>().WithParameterName("source");
	}

	[Fact]
	public void factory_create_rejects_a_null_as_of_source()
	{
		var act = () => EnrolmentEngineFactory.Create(ShippedSource(), (Func<DateOnly>)null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("asOf");
	}

	[Fact]
	public void factory_directory_create_rejects_a_null_as_of_source()
	{
		var act = () => EnrolmentEngineFactory.Create(Harness.WorkflowsDir, Harness.DataDir, (Func<DateOnly>)null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("asOf");
	}

	[Fact]
	public void factory_create_validates_the_date_source_before_opening_policy_data()
	{
		var source = new CallTrackingDataSource();

		var act = () => EnrolmentEngineFactory.Create(source, (Func<DateOnly>)null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("asOf");
		source.OpenCalls.Should().Be(0, "invalid arguments must be rejected before policy I/O starts");
	}

	private sealed class CallTrackingDataSource : IEnrolmentDataSource
	{
		public int OpenCalls { get; private set; }

		public IReadOnlyList<WorkflowContent> OpenWorkflows() => Open<IReadOnlyList<WorkflowContent>>();

		public Stream OpenWorkflowSchema() => Open<Stream>();

		public Stream OpenCatalogue() => Open<Stream>();

		public Stream OpenCatalogueSchema() => Open<Stream>();

		public Stream OpenQualifications() => Open<Stream>();

		public Stream OpenQualificationsSchema() => Open<Stream>();

		public Stream OpenThresholds() => Open<Stream>();

		public Stream OpenThresholdsSchema() => Open<Stream>();

		public Stream OpenTransitionMatrix() => Open<Stream>();

		private T Open<T>()
		{
			++OpenCalls;
			throw new InvalidOperationException("Policy data must not be opened for an invalid factory call.");
		}
	}

	// --- EnrolmentPolicy / EnrolmentPolicyDescriptor / EnrolmentPolicyDefinition ---

	[Fact]
	public void policy_descriptor_rejects_the_default_identifier()
	{
		var act = static () => new EnrolmentPolicyDescriptor(default, "Standard");

		act.Should().Throw<ArgumentException>().WithParameterName("id");
	}

	[Fact]
	public void policy_descriptor_rejects_a_blank_display_name()
	{
		var act = static () => new EnrolmentPolicyDescriptor(new("standard"), " ");

		act.Should().Throw<ArgumentException>().WithParameterName("displayName");
	}

	[Fact]
	public void policy_rejects_a_null_descriptor()
	{
		var act = () => new EnrolmentPolicy(null!, Harness.ShippedEngine());

		act.Should().Throw<ArgumentNullException>().WithParameterName("descriptor");
	}

	[Fact]
	public void policy_rejects_a_null_engine()
	{
		var act = static () => new EnrolmentPolicy(new(new("standard"), "Standard"), null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("engine");
	}

	[Fact]
	public void policy_definition_rejects_the_default_identifier()
	{
		var act = () => new EnrolmentPolicyDefinition(default, "Standard", ShippedSource());

		act.Should().Throw<ArgumentException>().WithParameterName("id");
	}

	[Fact]
	public void policy_definition_rejects_a_null_source()
	{
		var act = static () => new EnrolmentPolicyDefinition(new("standard"), "Standard", null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("source");
	}

	// --- Subject / EnrolmentPolicyId strongly-typed identifiers ---

	[Fact]
	public void subject_rejects_a_null_value_with_argument_null_exception()
	{
		var act = static () => new Subject(null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("value");
	}

	[Fact]
	public void subject_rejects_invalid_non_null_text_with_argument_exception()
	{
		var act = static () => new Subject("Not Valid!");

		act.Should().Throw<ArgumentException>().WithParameterName("value");
	}

	[Fact]
	public void policy_id_rejects_a_null_value_with_argument_null_exception()
	{
		var act = static () => new EnrolmentPolicyId(null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("value");
	}

	[Fact]
	public void policy_id_rejects_invalid_non_null_text_with_argument_exception()
	{
		var act = static () => new EnrolmentPolicyId("Not Valid!");

		act.Should().Throw<ArgumentException>().WithParameterName("value");
	}
}
