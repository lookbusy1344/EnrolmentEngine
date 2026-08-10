namespace EnrolmentRules.Web.Api;

using Domain;

/// <summary>
///     Boundary/resource limits for the posted <c>/api/enrolment/evaluate</c> snapshot, checked before
///     <see cref="EnrolmentApiMapper" /> so an oversized document is rejected cheaply rather than mapped
///     and evaluated. These are transport-level resource limits sized from the actual vocabulary
///     (<see cref="GcseSubjects" />, the catalogue), not compiled policy — a structurally valid grade or
///     subject key within these bounds is still checked by <see cref="StudentValidator" /> once mapped.
/// </summary>
public static class EnrolmentApiBoundaryValidator
{
	/// <summary>Generous upper bound on any single posted token (a subject key, hobby tag, qualification type/grade string).</summary>
	private const int MaxTokenLength = 100;

	/// <summary>Prior qualifications have no catalogue-sized vocabulary to bound against, so this is a flat resource cap.</summary>
	private const int MaxPriorQualifications = 50;

	/// <summary>Hobbies are free-text tags with no catalogue vocabulary, so this is a flat resource cap.</summary>
	private const int MaxHobbies = 50;

	/// <summary>Every problem found, in document order; empty means the snapshot is within bounds.</summary>
	public static IReadOnlyList<string> Validate(EnrolmentEvaluateRequest request, CatalogueData catalogue)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(catalogue);

		return [
			.. CountLimit(request.Gcses.Count, GcseSubjects.Known.Count, "gcses"),
			.. request.Gcses.SelectMany(static (row, index) => TokenLimit(row.Subject, $"gcses[{index}].subject")),
			.. CountLimit(request.ChosenALevels.Count, catalogue.Subjects.Count, "chosen_a_levels"),
			.. request.ChosenALevels.SelectMany(static (value, index) => TokenLimit(value, $"chosen_a_levels[{index}]")),
			.. CountLimit(request.PriorQualifications.Count, MaxPriorQualifications, "prior_qualifications"),
			.. request.PriorQualifications.SelectMany(static (row, index) => PriorQualificationTokenLimits(row, index)),
			.. CountLimit(request.Hobbies.Count, MaxHobbies, "hobbies"),
			.. request.Hobbies.SelectMany(static (value, index) => TokenLimit(value, $"hobbies[{index}]")),
		];
	}

	private static IEnumerable<string> PriorQualificationTokenLimits(EvaluatePriorQualificationRow row, int index) => [
		.. TokenLimit(row.Subject, $"prior_qualifications[{index}].subject"),
		.. TokenLimit(row.Type, $"prior_qualifications[{index}].type"),
		.. TokenLimit(row.Grade, $"prior_qualifications[{index}].grade"),
	];

	private static IEnumerable<string> CountLimit(int actual, int max, string fieldName) =>
		actual > max ? [$"{fieldName} has {actual} entries, exceeding the maximum of {max}"] : [];

	private static IEnumerable<string> TokenLimit(string? value, string fieldName) =>
		value is not null && value.Length > MaxTokenLength
			? [$"{fieldName} exceeds the maximum length of {MaxTokenLength} characters"]
			: [];
}
