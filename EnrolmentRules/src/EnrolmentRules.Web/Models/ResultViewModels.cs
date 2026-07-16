namespace EnrolmentRules.Web.Models;

using Domain;

/// <summary>
///     The result the page renders for the current session snapshot: either the validation errors
///     <see cref="Domain.StudentValidator" /> raised against the mapped <c>StudentInput</c>, or the full
///     per-subject <see cref="ExplainedResult" /> from <c>TryExplain</c>. Never both — an invalid snapshot
///     shows no (possibly stale) recommendations.
/// </summary>
public sealed record EnrolmentResultsViewModel(EquatableArray<string> ValidationErrors, ExplainedResult? Result)
{
	public bool IsValid => ValidationErrors.Count == 0;

	public static EnrolmentResultsViewModel From(ValidatedEvaluation<ExplainedResult> evaluation)
	{
		ArgumentNullException.ThrowIfNull(evaluation);
		return new(evaluation.Validation.Errors, evaluation.Value);
	}
}
