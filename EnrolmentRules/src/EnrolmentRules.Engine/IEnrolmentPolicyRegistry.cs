namespace EnrolmentRules.Engine;

using Domain;

/// <summary>Display metadata for one registered policy — the identifier plus the label front ends render.</summary>
public sealed record EnrolmentPolicyDescriptor(EnrolmentPolicyId Id, string DisplayName);

/// <summary>
///     One registered policy: its descriptor plus the immutable, already-bootstrapped engine it owns. A
///     <c>class</c> rather than a <c>record</c> because <see cref="Engine" /> is not value-equatable (JSV01).
/// </summary>
public sealed class EnrolmentPolicy(EnrolmentPolicyDescriptor descriptor, IEnrolmentEngine engine)
{
	public EnrolmentPolicyDescriptor Descriptor { get; } = descriptor;

	public IEnrolmentEngine Engine { get; } = engine;
}

/// <summary>
///     One policy's data source declaration for <see cref="EnrolmentPolicyRegistry" /> construction: the
///     identifier, its display name, and the (possibly <see cref="Hosting.OverlayEnrolmentDataSource" />-wrapped)
///     source the registry builds it from. A <c>class</c> rather than a <c>record</c> because
///     <see cref="Source" /> is not value-equatable (JSV01).
/// </summary>
public sealed class EnrolmentPolicyDefinition(EnrolmentPolicyId id, string displayName, IEnrolmentDataSource source)
{
	public EnrolmentPolicyId Id { get; } = id;

	public string DisplayName { get; } = displayName;

	public IEnrolmentDataSource Source { get; } = source;
}

/// <summary>
///     A non-destructive comparison of one shared <see cref="StudentInput" /> against one policy (§2.3):
///     the selected descriptor, the selected policy's explained evaluation, one <see cref="ChosenSubjectStatus" />
///     per <c>chosen_a_levels</c> entry (in the input's own order, none dropped), and the selected policy's
///     effective final-programme bounds. Never mutates the input; a strict enrolment decision still goes
///     through <see cref="IEnrolmentEvaluator.EvaluateValidated(StudentInput, CancellationToken)" /> or
///     <see cref="EnrolmentEngine.ValidateFinalProgramme(StudentInput, CancellationToken)" />, which remain
///     unweakened.
/// </summary>
public sealed record PolicyComparisonResult(
	EnrolmentPolicyDescriptor Descriptor,
	ExplainedResult Explanation,
	EquatableArray<ChosenSubjectStatus> ChoiceStatuses,
	int MinChosenALevels,
	int MaxChosenALevels);

/// <summary>
///     The library's single source of truth for registered policy/rule-set snapshots: their identifiers,
///     display labels, the default policy, and their immutable engine instances. Selection is always an
///     explicit per-call lookup by <see cref="EnrolmentPolicyId" /> — the registry itself holds no mutable
///     "current policy" state, so concurrent callers using different policies never interfere with one
///     another.
/// </summary>
public interface IEnrolmentPolicyRegistry
{
	/// <summary>Every registered policy's descriptor, in registration order (the order front ends should render).</summary>
	IReadOnlyList<EnrolmentPolicyDescriptor> Descriptors { get; }

	/// <summary>The identifier a caller gets when none is supplied.</summary>
	EnrolmentPolicyId DefaultPolicyId { get; }

	/// <summary>The registered policy for <paramref name="id" />.</summary>
	/// <exception cref="UnknownEnrolmentPolicyException"><paramref name="id" /> is not registered.</exception>
	EnrolmentPolicy GetPolicy(EnrolmentPolicyId id);

	/// <summary>Look up the registered policy for <paramref name="id" /> without throwing.</summary>
	bool TryGetPolicy(EnrolmentPolicyId id, out EnrolmentPolicy policy);

	/// <summary>
	///     Compare <paramref name="student" /> against the policy registered for <paramref name="id" />
	///     (§2.3). Structural facts validation (<see cref="StudentValidator.ValidateFacts" /> — everything
	///     except catalogue membership of the chosen A-levels) still gates the call: a malformed document
	///     returns those errors with a <c>null</c> value, exactly like the other <c>*Validated</c> entry
	///     points. A chosen subject absent from the selected policy's catalogue is <em>not</em> a validation
	///     failure here — it is classified <see cref="ChoiceStatus.NotOffered" /> in the result.
	/// </summary>
	/// <exception cref="UnknownEnrolmentPolicyException"><paramref name="id" /> is not registered.</exception>
	ValidatedEvaluation<PolicyComparisonResult> Compare(
		EnrolmentPolicyId id, StudentInput student, CancellationToken cancellationToken = default);
}

/// <summary>
///     Base type for policy-registry problems, so CLI/HTTP callers can catch one type for "the requested
///     policy could not be used" regardless of whether it failed to build or was never registered.
/// </summary>
public abstract class EnrolmentPolicyRegistryException : Exception
{
	protected EnrolmentPolicyRegistryException() { }

	protected EnrolmentPolicyRegistryException(string message) : base(message) { }

	protected EnrolmentPolicyRegistryException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
///     A registered policy's engine failed to build at startup — schema validation, catalogue/threshold
///     load-time invariants, or workflow probe/lint. Wraps the original exception with the offending
///     policy's identifier so a multi-policy startup failure names which policy broke, not a bare inner
///     message that reads as though it were the only policy.
/// </summary>
public sealed class EnrolmentPolicyBuildException : EnrolmentPolicyRegistryException
{
	public EnrolmentPolicyBuildException() { }

	public EnrolmentPolicyBuildException(string message) : base(message) { }

	public EnrolmentPolicyBuildException(string message, Exception innerException) : base(message, innerException) { }

	public EnrolmentPolicyBuildException(EnrolmentPolicyId policyId, Exception innerException)
		: base($"Policy '{policyId}' failed to build: {innerException.Message}", innerException) =>
		PolicyId = policyId;

	/// <summary>The policy identifier being built when the failure occurred; default when unset.</summary>
	public EnrolmentPolicyId PolicyId { get; }
}

/// <summary>
///     Registry construction was given an invalid definition set (duplicate/blank identifier or display
///     name, an unknown default, or an empty list) — a configuration mistake, not a per-policy build failure.
/// </summary>
public sealed class EnrolmentPolicyConfigurationException : EnrolmentPolicyRegistryException
{
	public EnrolmentPolicyConfigurationException() { }

	public EnrolmentPolicyConfigurationException(string message) : base(message) { }

	public EnrolmentPolicyConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
///     A lookup named a policy identifier the registry does not carry. Typed distinctly from
///     <see cref="EnrolmentPolicyConfigurationException" /> so a caller can map it directly to a CLI usage
///     error or an HTTP 400, and distinctly from <see cref="EnrolmentPolicyBuildException" /> so "policy
///     doesn't exist" is never confused with "policy exists but failed to build".
/// </summary>
public sealed class UnknownEnrolmentPolicyException : EnrolmentPolicyRegistryException
{
	public UnknownEnrolmentPolicyException() { }

	public UnknownEnrolmentPolicyException(string message) : base(message) { }

	public UnknownEnrolmentPolicyException(string message, Exception innerException) : base(message, innerException) { }

	public UnknownEnrolmentPolicyException(EnrolmentPolicyId policyId, IReadOnlyList<EnrolmentPolicyId> known)
		: base($"Policy '{policyId}' is not registered. Known policies: {string.Join(", ", known.Select(static id => id.Value))}.") =>
		PolicyId = policyId;

	/// <summary>The unrecognised identifier that was looked up; default when unset.</summary>
	public EnrolmentPolicyId PolicyId { get; }
}
