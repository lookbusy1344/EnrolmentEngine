namespace EnrolmentRules.Domain;

/// <summary>
///     Marks a value type authored on purpose above <c>CodeStyle_StructSize</c>'s 24-byte guideline
///     ceiling, with the justification for why it is exempt. Attach only after review; this is the
///     same "earns its place only by review" allowance the test's own exclusions apply elsewhere.
///     Do not apply this attribute without explicit user permission — get agreement before exempting
///     a type from the size guideline; this is the reviewed exception, not a routine escape hatch.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class LargeStructAttribute(string justification) : Attribute
{
	public string Justification { get; } = justification;
}
