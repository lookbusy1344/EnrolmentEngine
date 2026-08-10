namespace EnrolmentRules.Engine;

/// <summary>
///     A reloadable holder for the bootstrapped <see cref="IEnrolmentEngine" />. Policy edits on disk take
///     effect only after <see cref="Reload" /> rebuilds the engine; in-flight evaluations on the
///     previous instance complete normally.
/// </summary>
public interface IEnrolmentEngineFactory
{
	/// <summary>The engine instance callers should evaluate against right now.</summary>
	IEnrolmentEngine Current { get; }

	/// <summary>
	///     Rebuild the engine from the bound data source. On success, swaps <see cref="Current" />; on
	///     startup failure the previous instance is left in place and the exception is propagated.
	/// </summary>
	void Reload(CancellationToken cancellationToken = default);
}
