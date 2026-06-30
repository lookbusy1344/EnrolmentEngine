namespace EnrolmentRules.Engine;

/// <summary>A named workflow stream returned by <see cref="IEnrolmentDataSource.OpenWorkflows" />.</summary>
/// <remarks>The consumer owns the content stream and must dispose this instance after loading it.</remarks>
public sealed class WorkflowContent : IDisposable
{
	public WorkflowContent(string fileName, Stream content)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		ArgumentNullException.ThrowIfNull(content);
		FileName = fileName;
		Content = content;
	}

	public string FileName { get; }

	public Stream Content { get; }

	public void Dispose() => Content.Dispose();

	public void Deconstruct(out string fileName, out Stream content) => (fileName, content) = (FileName, Content);
}
