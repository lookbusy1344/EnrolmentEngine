namespace EnrolmentRules.Web.Models;

using Infrastructure;
using Subject = Domain.Subject;

/// <summary>
///     The editable input snapshot held in the ASP.NET session for one anonymous browsing session —
///     never the engine's derived output (see <see cref="Services.EnrolmentSessionStore" />). Rows may be
///     blank (<see cref="GcseRow.IsEmpty" />, <see cref="PriorQualificationRow.IsEmpty" />); the mapper,
///     not this record, is responsible for filtering them before building a <c>StudentInput</c>.
/// </summary>
public sealed record EnrolmentSession(
	string StudentId,
	DateOnly? DateOfBirth,
	EquatableArray<GcseRow> Gcses,
	EquatableArray<PriorQualificationRow> PriorQualifications,
	EquatableArray<string> Hobbies,
	EquatableArray<Subject> ChosenALevels)
{
	/// <summary>A fresh snapshot with no facts recorded yet, keyed to the given anonymous session id.</summary>
	public static EnrolmentSession Empty(string studentId) => new(studentId, null, [], [], [], []);
}
