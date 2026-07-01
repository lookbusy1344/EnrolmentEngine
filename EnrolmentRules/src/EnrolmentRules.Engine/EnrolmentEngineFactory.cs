namespace EnrolmentRules.Engine;

/// <summary>
///     Thread-safe factory that rebuilds <see cref="EnrolmentEngine" /> from a fixed
///     <see cref="IEnrolmentDataSource" /> when policy changes on disk.
/// </summary>
public sealed class EnrolmentEngineFactory : IEnrolmentEngineFactory, IDisposable
{
	private readonly Func<DateOnly> asOf;
	private readonly SemaphoreSlim reloadGate = new(1, 1);
	private readonly IEnrolmentDataSource source;
	private IEnrolmentEngine current;

	private EnrolmentEngineFactory(IEnrolmentDataSource source, Func<DateOnly> asOf, IEnrolmentEngine initial)
	{
		this.source = source;
		this.asOf = asOf;
		current = initial;
	}

	public void Dispose() => reloadGate.Dispose();

	/// <inheritdoc />
	public IEnrolmentEngine Current => Volatile.Read(ref current);

	/// <inheritdoc />
	public void Reload(CancellationToken cancellationToken = default)
	{
		reloadGate.Wait(cancellationToken);
		try {
			var rebuilt = EnrolmentEngine.Create(source, asOf, cancellationToken);
			Volatile.Write(ref current, rebuilt);
		}
		finally {
			_ = reloadGate.Release();
		}
	}

	/// <summary>Bootstrap a factory from directory paths and a fixed reference date.</summary>
	public static EnrolmentEngineFactory Create(
		string workflowsDirectory,
		string dataDirectory,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
		=> Create(new DirectoryDataSource(workflowsDirectory, dataDirectory), () => asOf, cancellationToken);

	/// <summary>Bootstrap a factory from directory paths and a live reference-date source.</summary>
	public static EnrolmentEngineFactory Create(
		string workflowsDirectory,
		string dataDirectory,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
		=> Create(new DirectoryDataSource(workflowsDirectory, dataDirectory), asOf, cancellationToken);

	/// <summary>Bootstrap a factory from a stream-backed data source and a fixed reference date.</summary>
	public static EnrolmentEngineFactory Create(
		IEnrolmentDataSource source,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
		=> Create(source, () => asOf, cancellationToken);

	/// <summary>Bootstrap a factory from a stream-backed data source and a reference-date source.</summary>
	public static EnrolmentEngineFactory Create(
		IEnrolmentDataSource source,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(asOf);
		var engine = EnrolmentEngine.Create(source, asOf, cancellationToken);
		return new(source, asOf, engine);
	}
}
