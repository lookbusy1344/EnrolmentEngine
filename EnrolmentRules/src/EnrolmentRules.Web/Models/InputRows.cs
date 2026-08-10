namespace EnrolmentRules.Web.Models;

using Domain;

/// <summary>
///     One repeatable GCSE entry row on the form: a free-typed subject key and its 1–9 grade. Kept
///     separate from <see cref="GcseResult" /> so a partially-filled row (e.g. a subject chosen but no
///     grade selected yet) can be represented and later filtered by <see cref="IsEmpty" /> rather than
///     failing model binding.
/// </summary>
public readonly record struct GcseRow(string? Subject, int? Grade)
{
	/// <summary>Whether the row has neither a subject nor a grade — a blank row added by the "add row" button.</summary>
	public bool IsEmpty => string.IsNullOrWhiteSpace(Subject) && Grade is null;
}

/// <summary>
///     One repeatable prior-qualification entry row on the form: a free-typed subject, the qualification
///     type and its raw grade token. Kept separate from <see cref="Qualification" /> for the same reason
///     as <see cref="GcseRow" /> — a partially-filled row is representable, not a binding failure.
/// </summary>
public readonly record struct PriorQualificationRow(string? Subject, QualificationType? Type, string? Grade)
{
	/// <summary>Whether the row has no subject, no type and no grade.</summary>
	public bool IsEmpty => string.IsNullOrWhiteSpace(Subject) && Type is null && string.IsNullOrWhiteSpace(Grade);
}
