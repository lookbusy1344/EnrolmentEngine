namespace EnrolmentRules.Engine;

/// <summary>
///     A reloadable holder for the bootstrapped <see cref="IEnrolmentEngine" />. Policy edits on disk take
///     effect only after <see cref="Reload" /> rebuilds the engine; in-flight evaluations on the
///     previous instance complete normally.
/// </summary>
public interface IEnrolmentEngineFactory
{
	/// <summary>
	///     The engine instance callers should evaluate against right now. Resolve this once per unit of work
	///     (e.g. once per request) and reuse that reference — a caller that re-reads <see cref="Current" />
	///     between two related calls (say, validating against <c>Catalogue</c> and then calling
	///     <c>EvaluateValidated</c>) can straddle a <see cref="Reload" /> and see two different engines. A
	///     host that needs cross-call consistency resolves <see cref="IEnrolmentEngineFactory" /> itself and
	///     holds the snapshot, rather than going through a proxy that re-resolves per call (as
	///     <c>ReloadingEnrolmentEngineProxy</c> does).
	/// </summary>
	IEnrolmentEngine Current { get; }

	/// <summary>
	///     Rebuild the engine from the bound data source. On success, swaps <see cref="Current" />; on
	///     startup failure the previous instance is left in place and the exception is propagated.
	/// </summary>
	void Reload(CancellationToken cancellationToken = default);
}
