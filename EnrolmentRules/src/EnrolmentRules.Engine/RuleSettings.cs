namespace EnrolmentRules.Engine;

using Domain;
using RulesEngine.Models;

/// <summary>
///     The engine's <see cref="ReSettings" />. RulesEngine (via System.Linq.Dynamic.Core) only permits a
///     lambda to call methods on types it has been told are safe, so the host accessor types the workflow
///     expressions invoke (e.g. <see cref="GcseFacts" />) must be registered as custom types. Built once
///     and shared across the reusable engine.
/// </summary>
[CLSCompliant(false)]
public static class RuleSettings
{
	public static ReSettings Default { get; } = new() {
		CustomTypes = [typeof(GcseFacts), typeof(PolicyFacts), typeof(RatingFacts), typeof(Thresholds), typeof(ALevelGrade)],
	};
}
