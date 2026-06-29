namespace EnrolmentRules.Engine;

/// <summary>
///     Thread-safe factory that rebuilds <see cref="EnrolmentEngine" /> from a fixed
///     <see cref="IEnrolmentDataSource" /> when policy changes on disk.
/// </summary>
public sealed class EnrolmentEngineFactory : IEnrolmentEngineFactory
{
	private readonly Func<DateOnly> asOf;
	private readonly IEnrolmentDataSource source;
	private IEnrolmentEngine current;

	private EnrolmentEngineFactory(IEnrolmentDataSource source, Func<DateOnly> asOf, IEnrolmentEngine initial)
	{
		this.source = source;
		this.asOf = asOf;
		current = initial;
	}

	/// <inheritdoc />
	public IEnrolmentEngine Current => Volatile.Read(ref current);

	/// <inheritdoc />
	public async Task ReloadAsync(CancellationToken cancellationToken = default)
	{
		var rebuilt = await EnrolmentEngine.CreateAsync(source, asOf, cancellationToken).ConfigureAwait(false);
		Volatile.Write(ref current, rebuilt);
	}

	/// <summary>Bootstrap a factory from directory paths and a fixed reference date.</summary>
	public static Task<EnrolmentEngineFactory> CreateAsync(
		string workflowsDirectory,
		string dataDirectory,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
		=> CreateAsync(new DirectoryDataSource(workflowsDirectory, dataDirectory), () => asOf, cancellationToken);

	/// <summary>Bootstrap a factory from directory paths and a live reference-date source.</summary>
	public static Task<EnrolmentEngineFactory> CreateAsync(
		string workflowsDirectory,
		string dataDirectory,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
		=> CreateAsync(new DirectoryDataSource(workflowsDirectory, dataDirectory), asOf, cancellationToken);

	/// <summary>Bootstrap a factory from a stream-backed data source and a fixed reference date.</summary>
	public static Task<EnrolmentEngineFactory> CreateAsync(
		IEnrolmentDataSource source,
		DateOnly asOf,
		CancellationToken cancellationToken = default)
		=> CreateAsync(source, () => asOf, cancellationToken);

	/// <summary>Bootstrap a factory from a stream-backed data source and a reference-date source.</summary>
	public static async Task<EnrolmentEngineFactory> CreateAsync(
		IEnrolmentDataSource source,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(asOf);
		var engine = await EnrolmentEngine.CreateAsync(source, asOf, cancellationToken).ConfigureAwait(false);
		return new(source, asOf, engine);
	}
}
