namespace EnrolmentRules.Web.Services;

using Engine;

/// <summary>
///     Resolves the policy identifier a request names — a query parameter — against the shared
///     <see cref="IEnrolmentPolicyRegistry" />. A malformed or unregistered identifier is never silently
///     swapped for the default: callers get <c>false</c> and report a bounded 400 error.
/// </summary>
public static class EnrolmentPolicySelector
{
	/// <summary>Resolve <paramref name="requested" /> (a raw query value; blank/null means "use the default").</summary>
	public static bool TryResolve(IEnrolmentPolicyRegistry registry, string? requested, out EnrolmentPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(registry);

		if (string.IsNullOrWhiteSpace(requested)) {
			policy = registry.GetPolicy(registry.DefaultPolicyId);
			return true;
		}

		if (EnrolmentPolicyId.TryParse(requested, out var id) && registry.TryGetPolicy(id, out var resolved)) {
			policy = resolved;
			return true;
		}

		policy = null!;
		return false;
	}
}
