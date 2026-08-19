namespace EnrolmentRules.Domain.CodeStyle;

/// <summary>
///     Marks a value type authored on purpose above <c>CodeStyle_StructSize</c>'s 24-byte guideline
///     ceiling. <paramref name="reason" /> carries the justification alongside the declaration
///     itself rather than in a separate allowlist, so the exception cannot drift from why it was
///     made. Do not apply this attribute without explicit user permission — this is the reviewed
///     exception, not a routine escape hatch.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class LargeStructAttribute(string reason) : Attribute
{
	/// <summary>Why this value type is exempt from the size guideline.</summary>
	public string Reason { get; } = reason;
}
