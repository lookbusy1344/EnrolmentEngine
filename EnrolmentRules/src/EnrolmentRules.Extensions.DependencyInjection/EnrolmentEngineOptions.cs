namespace EnrolmentRules.Extensions.DependencyInjection;

using Engine;

/// <summary>
///     Options for registering a reusable <see cref="EnrolmentEngine" /> into a dependency-injection
///     container. The engine itself remains stateless; these values identify the shipped data set and the
///     reference date to bind at startup.
/// </summary>
public sealed class EnrolmentEngineOptions
{
	public string WorkflowsDirectory { get; set; } = string.Empty;

	public string DataDirectory { get; set; } = string.Empty;

	public DateOnly? FixedAsOf { get; private set; }

	public TimeProvider? TimeProvider { get; private set; }

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
}
