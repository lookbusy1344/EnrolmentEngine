namespace EnrolmentRules.Web.Models;

using Domain;

/// <summary>
///     The mutable model-binding target for one GCSE form row. <see cref="GcseRow" /> is a
///     <c>readonly record struct</c> for value semantics elsewhere in the pipeline; ASP.NET's default
///     complex-object model binder does not bind record structs from indexed form fields (e.g.
///     <c>Gcses[0].Subject</c>) the way it binds a plain settable-property class, so the page handler
///     binds to this shape and converts to <see cref="GcseRow" /> explicitly.
/// </summary>
public sealed class GcseRowBinding
{
	public string? Subject { get; set; }

	/// <summary>
	///     The raw posted grade, bound as a <see cref="double" /> rather than an <see cref="int" /> so a typed
	///     decimal reaches <see cref="ToRow" /> to be rounded instead of failing model binding and silently
	///     arriving as "no grade".
	/// </summary>
	public double? Grade { get; set; }

	public GcseRow ToRow() => new(Subject, Grade.HasValue ? Thresholds.NormalizeGcseGrade(Grade.Value) : null);

	public static GcseRowBinding FromRow(GcseRow row) => new() { Subject = row.Subject, Grade = row.Grade };
}

/// <summary>The mutable model-binding target for one prior-qualification form row. See <see cref="GcseRowBinding" /> for why.</summary>
public sealed class PriorQualificationRowBinding
{
	public string? Subject { get; set; }

	public QualificationType? Type { get; set; }

	public string? Grade { get; set; }

	public PriorQualificationRow ToRow() => new(Subject, Type, Grade);

	public static PriorQualificationRowBinding FromRow(PriorQualificationRow row) =>
		new() { Subject = row.Subject, Type = row.Type, Grade = row.Grade };
}
