namespace EnrolmentRules.Extensions.DependencyInjection;

/// <summary>
///     Options for registering a multi-policy <see cref="EnrolmentPolicyRegistry" /> into a
///     dependency-injection container via <see cref="ServiceCollectionExtensions.AddEnrolmentPolicies" />.
///     A separate path from <see cref="EnrolmentEngineOptions" /> deliberately: registering a policy here
///     never also registers an ambiguous container-wide <see cref="IEnrolmentEngine" />, so a consumer must
///     ask the registry for a named policy rather than accidentally resolving Standard's engine for an
///     Elite request (or vice versa).
/// </summary>
public sealed class EnrolmentPolicyOptions
{
	private readonly List<EnrolmentPolicyDefinition> definitions = [];

	public EnrolmentPolicyId? DefaultPolicyId { get; private set; }

	public DateOnly? FixedAsOf { get; private set; }

	public TimeProvider? TimeProvider { get; private set; }

	/// <summary>Register a policy and mark it the default a caller gets when no identifier is supplied.</summary>
	public EnrolmentPolicyOptions UseDefault(string id, string displayName, IEnrolmentDataSource source)
	{
		var policyId = new EnrolmentPolicyId(id);
		_ = Add(policyId, displayName, source);
		DefaultPolicyId = policyId;
		return this;
	}

	/// <summary>Register an additional, non-default policy.</summary>
	public EnrolmentPolicyOptions Add(string id, string displayName, IEnrolmentDataSource source) =>
		Add(new EnrolmentPolicyId(id), displayName, source);

	private EnrolmentPolicyOptions Add(EnrolmentPolicyId id, string displayName, IEnrolmentDataSource source)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
		ArgumentNullException.ThrowIfNull(source);
		definitions.Add(new(id, displayName, source));
		return this;
	}

	/// <summary>Bind every policy to one fixed reference date; every evaluation uses <paramref name="asOf" />.</summary>
	public EnrolmentPolicyOptions UseFixedAsOf(DateOnly asOf)
	{
		FixedAsOf = asOf;
		TimeProvider = null;
		return this;
	}

	/// <summary>
	///     Bind every policy to a live clock, resolved per evaluation — see
	///     <see cref="EnrolmentEngineOptions.UseTimeProvider" /> for the same rationale.
	/// </summary>
	public EnrolmentPolicyOptions UseTimeProvider(TimeProvider? timeProvider = null)
	{
		TimeProvider = timeProvider ?? TimeProvider.System;
		FixedAsOf = null;
		return this;
	}

	private Func<DateOnly> AsOfSource()
	{
		if (FixedAsOf is DateOnly fixedAsOf) {
			return () => fixedAsOf;
		}

		var provider = TimeProvider ?? TimeProvider.System;
		return () => DateOnly.FromDateTime(provider.GetLocalNow().DateTime);
	}

	/// <summary>Run the full multi-policy startup recipe for these options and return the built registry.</summary>
	internal EnrolmentPolicyRegistry CreateRegistry(CancellationToken cancellationToken = default)
	{
		if (definitions.Count == 0) {
			throw new ArgumentException("At least one policy must be registered (call UseDefault or Add).");
		}

		if (DefaultPolicyId is not EnrolmentPolicyId defaultId) {
			throw new ArgumentException("A default policy must be selected via UseDefault.");
		}

		return new(definitions, defaultId, AsOfSource(), cancellationToken);
	}
}
