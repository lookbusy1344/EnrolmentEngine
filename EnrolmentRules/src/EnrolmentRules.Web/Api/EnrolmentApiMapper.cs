namespace EnrolmentRules.Web.Api;

using System.Diagnostics.CodeAnalysis;
using Domain;
using Models;
using Services;
using EquatableArray = Infrastructure.EquatableArray;
using Subject = Domain.Subject;

/// <summary>
///     Maps a posted <see cref="EnrolmentEvaluateRequest" /> to the engine's <c>StudentInput</c> by building
///     the same web-local <see cref="EnrolmentSession" /> shape <see cref="EnrolmentFormMapper" /> already
///     knows how to project, so the API and the Razor page share one mapping path. Implements no
///     relationship policy of its own — a subject/qualification-type token that cannot be parsed at all
///     fails the mapping outright (→ 400); a token that parses but fails a business rule (an out-of-range
///     grade, an unknown GCSE key) is left for <see cref="Domain.StudentValidator" /> to report as normal
///     validation feedback.
/// </summary>
public static class EnrolmentApiMapper
{
	private const string ApiStudentId = "api-request";

	public static bool TryToStudentInput(EnrolmentEvaluateRequest request, [NotNullWhen(true)] out StudentInput? input)
	{
		ArgumentNullException.ThrowIfNull(request);

		var priorQualificationRows = new List<PriorQualificationRow>();
		foreach (var row in request.PriorQualifications) {
			if (string.IsNullOrWhiteSpace(row.Type)) {
				priorQualificationRows.Add(new(row.Subject, null, row.Grade));
				continue;
			}

			if (!Enum.TryParse<QualificationType>(row.Type, true, out var type) || !Enum.IsDefined(type)) {
				input = null;
				return false;
			}

			priorQualificationRows.Add(new(row.Subject, type, row.Grade));
		}

		var chosenALevels = new List<Subject>();
		foreach (var value in request.ChosenALevels) {
			if (!Subject.TryParse(value, out var subject)) {
				input = null;
				return false;
			}

			chosenALevels.Add(subject);
		}

		var session = EnrolmentSession.Empty(ApiStudentId) with {
			DateOfBirth = request.DateOfBirth,
			Gcses = EquatableArray.CopyOf(request.Gcses.Select(static row => new GcseRow(row.Subject, row.Grade))),
			PriorQualifications = EquatableArray.CopyOf(priorQualificationRows),
			Hobbies = request.Hobbies,
			ChosenALevels = EquatableArray.CopyOf(chosenALevels),
		};

		input = EnrolmentFormMapper.ToStudentInput(session);
		return true;
	}
}
