namespace EnrolmentRules.Web.Services;

using Domain;
using Models;

/// <summary>
///     The pure boundary between the web-only <see cref="EnrolmentSession" />/form shapes and the engine's
///     <see cref="StudentInput" />. Never interprets hobby prefixes, exclusion policy or rating severity —
///     it only carries facts across the shape change, leaving validation to
///     <see cref="Domain.StudentValidator" /> at the engine boundary.
/// </summary>
public static class EnrolmentFormMapper
{
	/// <summary>
	///     Project the session snapshot to the engine's input shape. Blank rows (<see cref="GcseRow.IsEmpty" />,
	///     <see cref="PriorQualificationRow.IsEmpty" />) are dropped; a non-blank row with a missing piece
	///     (e.g. a subject with no grade) is mapped through as its boundary value (grade <c>0</c>, blank
	///     grade token) so <see cref="Domain.StudentValidator" /> reports it, rather than the mapper silently
	///     deciding what counts as valid.
	/// </summary>
	public static StudentInput ToStudentInput(EnrolmentSession session)
	{
		ArgumentNullException.ThrowIfNull(session);

		var gcses = new Dictionary<string, int>();
		foreach (var row in session.Gcses) {
			if (row.IsEmpty) {
				continue;
			}

			gcses[row.Subject ?? string.Empty] = row.Grade ?? 0;
		}

		var priorQualifications = session.PriorQualifications
			.Where(static row => !row.IsEmpty)
			.Select(static row => new Qualification(row.Subject ?? string.Empty, row.Type ?? default, row.Grade ?? string.Empty));

		var hobbies = session.Hobbies.Where(static hobby => !string.IsNullOrWhiteSpace(hobby));

		return new(session.StudentId, gcses, hobbies.ToArray()) {
			DateOfBirth = session.DateOfBirth,
			ChosenALevels = EquatableArray.CopyOf(session.ChosenALevels),
			PriorQualifications = EquatableArray.CopyOf(priorQualifications.ToArray()),
		};
	}

	/// <summary>Apply a posted "save facts" edit to the current session, preserving the student id and chosen A-levels.</summary>
	public static EnrolmentSession Apply(SaveFactsInput input, EnrolmentSession current)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(current);

		return current with {
			DateOfBirth = input.DateOfBirth,
			Gcses = input.Gcses,
			PriorQualifications = input.PriorQualifications,
			Hobbies = input.Hobbies,
		};
	}
}
