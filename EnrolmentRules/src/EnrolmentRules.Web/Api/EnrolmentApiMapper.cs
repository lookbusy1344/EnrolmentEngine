namespace EnrolmentRules.Web.Api;

using System.Diagnostics.CodeAnalysis;
using Domain;
using Subject = Domain.Subject;

/// <summary>
///     Maps a posted <see cref="EnrolmentEvaluateRequest" /> straight to the engine's <c>StudentInput</c>.
///     Implements no relationship policy of its own: a blank row is dropped, a non-blank row with a missing
///     piece is carried through at its boundary value (grade <c>0</c>, blank grade token) so
///     <see cref="Domain.StudentValidator" /> reports it, and a subject/qualification-type token that cannot
///     be parsed at all fails the mapping outright (→ 400). A token that parses but breaks a business rule
///     (an out-of-range grade, an unknown GCSE key) is left for validation to report.
///     Null rows and duplicate GCSE subjects fail mapping without returning partial facts.
/// </summary>
public static class EnrolmentApiMapper
{
	private const string ApiStudentId = "api-request";

	public static bool TryToStudentInput(EnrolmentEvaluateRequest request, [NotNullWhen(true)] out StudentInput? input)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (!TryMapGcses(request.Gcses, out var gcses)
			|| !TryMapPriorQualifications(request.PriorQualifications, out var priorQualifications)) {
			input = null;
			return false;
		}

		var chosenALevels = new List<Subject>();
		foreach (var value in request.ChosenALevels) {
			if (!Subject.TryParse(value, out var subject)) {
				input = null;
				return false;
			}

			chosenALevels.Add(subject);
		}

		var hobbies = request.Hobbies.Where(static hobby => !string.IsNullOrWhiteSpace(hobby)).ToArray();

		input = new(ApiStudentId, gcses, hobbies) {
			DateOfBirth = request.DateOfBirth,
			ChosenALevels = EquatableArray.CopyOf(chosenALevels),
			PriorQualifications = EquatableArray.CopyOf(priorQualifications),
		};
		return true;
	}

	private static bool TryMapGcses(IReadOnlyList<EvaluateGcseRow?> rows, out Dictionary<string, int> mapped)
	{
		mapped = [];
		foreach (var row in rows) {
			if (row is null) {
				return false;
			}

			if (string.IsNullOrWhiteSpace(row.Subject) && row.Grade is null) {
				continue;
			}

			if (!mapped.TryAdd(row.Subject ?? string.Empty, row.Grade ?? 0)) {
				return false;
			}
		}

		return true;
	}

	private static bool TryMapPriorQualifications(
		IReadOnlyList<EvaluatePriorQualificationRow?> rows, out List<Qualification> mapped)
	{
		mapped = [];
		foreach (var row in rows) {
			if (row is null) {
				return false;
			}

			QualificationType? type = null;
			if (!string.IsNullOrWhiteSpace(row.Type)) {
				if (!Enum.TryParse<QualificationType>(row.Type, true, out var parsed) || !Enum.IsDefined(parsed)) {
					return false;
				}

				type = parsed;
			}

			if (string.IsNullOrWhiteSpace(row.Subject) && type is null && string.IsNullOrWhiteSpace(row.Grade)) {
				continue;
			}

			mapped.Add(new(row.Subject ?? string.Empty, type ?? default, row.Grade ?? string.Empty));
		}

		return true;
	}
}
