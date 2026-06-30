namespace EnrolmentRules.Tests;

using Engine;
using FluentAssertions;

/// <summary>
///     Directory-backed workflow opening must not leak streams when a later file open fails part-way
///     through materialization.
/// </summary>
public sealed class DirectoryDataSourceTests
{
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
