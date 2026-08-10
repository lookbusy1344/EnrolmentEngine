namespace EnrolmentRules.Tests;

using AwesomeAssertions;

/// <summary>
///     Directory-backed workflow opening must not leak streams when a later file open fails part-way
///     through materialization.
/// </summary>
public sealed class DirectoryDataSourceTests
{
	[Fact]
	public void workflow_content_uses_a_named_public_type()
	{
		var returnType = typeof(IEnrolmentDataSource).GetMethod(nameof(IEnrolmentDataSource.OpenWorkflows))!.ReturnType;

		returnType.GetGenericArguments().Should().ContainSingle()
			.Which.Should().Be<WorkflowContent>();
	}

	[Fact]
	public void workflow_content_disposes_its_stream()
	{
		using var stream = new TrackingStream();

		new WorkflowContent("workflow.yaml", stream).Dispose();

		stream.IsDisposed.Should().BeTrue();
	}

	[Fact]
	public void workflow_content_rejects_an_invalid_file_name()
	{
		var act = () => new WorkflowContent(string.Empty, Stream.Null);

		act.Should().Throw<ArgumentException>().WithParameterName("fileName");
	}

	[Fact]
	public void workflow_content_rejects_null_content()
	{
		var act = () => new WorkflowContent("workflow.yaml", null!);

		act.Should().Throw<ArgumentNullException>().WithParameterName("content");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	public void constructor_rejects_an_invalid_workflows_directory(string? workflowsDirectory)
	{
		var act = () => new DirectoryDataSource(workflowsDirectory!, "data");

		act.Should().Throw<ArgumentException>().WithParameterName(nameof(workflowsDirectory));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	public void constructor_rejects_an_invalid_data_directory(string? dataDirectory)
	{
		var act = () => new DirectoryDataSource("workflows", dataDirectory!);

		act.Should().Throw<ArgumentException>().WithParameterName(nameof(dataDirectory));
	}

	[Fact]
	public void partially_opened_workflow_streams_are_disposed_when_a_later_open_fails()
	{
		using var opened = new TrackingStream();
		string[] files = ["a.yaml", "b.yaml"];

		var act = () => DirectoryDataSource.OpenWorkflowFiles(
			files,
			file => file switch {
				"a.yaml" => opened,
				"b.yaml" => throw new IOException("boom"),
				_ => throw new InvalidOperationException("unexpected file"),
			});

		act.Should().Throw<IOException>().WithMessage("boom");
		opened.IsDisposed.Should().BeTrue();
	}

	private sealed class TrackingStream : MemoryStream
	{
		public bool IsDisposed { get; private set; }

		protected override void Dispose(bool disposing)
		{
			IsDisposed = true;
			base.Dispose(disposing);
		}
	}
}
