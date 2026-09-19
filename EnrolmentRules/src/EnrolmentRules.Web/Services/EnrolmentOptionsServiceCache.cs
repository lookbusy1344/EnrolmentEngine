namespace EnrolmentRules.Web.Services;

using System.Collections.Concurrent;
using Engine;

/// <summary>
///     Application-owned cache of options projections. Policy instances are immutable snapshots, so one
///     projection is reusable for that instance; reference identity ensures a replacement registry carrying
///     the same policy id cannot inherit an older snapshot's catalogue, vocabulary or thresholds.
/// </summary>
internal sealed class EnrolmentOptionsServiceCache
{
	private readonly ConcurrentDictionary<EnrolmentPolicy, EnrolmentOptionsService> services =
		new(ReferenceEqualityComparer.Instance);

	public EnrolmentOptionsService Get(EnrolmentPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(policy);
		return services.GetOrAdd(policy, static resolved => new(resolved));
	}
}
