namespace EnrolmentRules.Extensions.DependencyInjection;

using Engine;

/// <summary>
///     Options for registering a reusable <see cref="EnrolmentEngine" /> into a dependency-injection
///     container. The engine itself remains stateless; these values identify the shipped data set and the
///     reference date to bind at startup.
/// </summary>
public sealed class EnrolmentEngineOptions
{
	public string WorkflowsDirectory { get; private set; } = string.Empty;

	public string DataDirectory { get; private set; } = string.Empty;

	public DateOnly? FixedAsOf { get; private set; }

	public TimeProvider? TimeProvider { get; private set; }

	/// <summary>Point the engine at the directory holding the workflow YAML files.</summary>
	public EnrolmentEngineOptions UseWorkflowsDirectory(string workflowsDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workflowsDirectory);
		WorkflowsDirectory = workflowsDirectory;
		return this;
	}

	/// <summary>Point the engine at the data directory holding the catalogue, thresholds and matrices.</summary>
	public EnrolmentEngineOptions UseDataDirectory(string dataDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
		DataDirectory = dataDirectory;
		return this;
	}

	/// <summary>Bind the engine to one fixed reference date; every evaluation uses <paramref name="asOf" />.</summary>
	public EnrolmentEngineOptions UseFixedAsOf(DateOnly asOf)
	{
		FixedAsOf = asOf;
		TimeProvider = null;
		return this;
	}

	/// <summary>
	///     Bind the engine to a live clock: the current local date from <paramref name="timeProvider" /> is
	///     resolved <em>per evaluation</em>, so the registered singleton tracks the wall clock and a student's
	///     age stays correct across day boundaries (rather than freezing at container build).
	/// </summary>
	public EnrolmentEngineOptions UseTimeProvider(TimeProvider? timeProvider = null)
	{
		TimeProvider = timeProvider ?? TimeProvider.System;
		FixedAsOf = null;
		return this;
	}

	/// <summary>
	///     The reference-date source the engine resolves on every evaluation. A fixed date returns a constant;
	///     a <see cref="System.TimeProvider" /> is read afresh each call so the date is never stale.
	/// </summary>
	internal Func<DateOnly> AsOfSource()
	{
		if (FixedAsOf is { } fixedAsOf) {
			return () => fixedAsOf;
		}

		var provider = TimeProvider ?? TimeProvider.System;
		return () => DateOnly.FromDateTime(provider.GetLocalNow().DateTime);
	}

	/// <summary>Validate the configured directories before bootstrap, so misconfiguration fails at the boundary.</summary>
	internal void Validate()
	{
		if (string.IsNullOrWhiteSpace(WorkflowsDirectory)) {
			throw new ArgumentException("Workflows directory must not be empty.", nameof(WorkflowsDirectory));
		}

		if (string.IsNullOrWhiteSpace(DataDirectory)) {
			throw new ArgumentException("Data directory must not be empty.", nameof(DataDirectory));
		}
	}

	/// <summary>Run the full startup recipe for these options and return a reusable engine.</summary>
	internal Task<EnrolmentEngine> CreateEngineAsync(CancellationToken cancellationToken = default)
	{
		Validate();
		return EnrolmentEngine.CreateAsync(WorkflowsDirectory, DataDirectory, AsOfSource(), cancellationToken);
	}

	/// <summary>Run the full startup recipe and return a reloadable factory.</summary>
	internal Task<EnrolmentEngineFactory> CreateFactoryAsync(CancellationToken cancellationToken = default)
	{
		Validate();
		return EnrolmentEngineFactory.CreateAsync(
			new DirectoryDataSource(WorkflowsDirectory, DataDirectory),
			AsOfSource(),
			cancellationToken);
	}
}
