namespace EnrolmentRules.Engine;

/// <summary>
///     A stable wire identifier for a registered policy/rule-set snapshot (§ policy registry): the stable
///     lowercase ASCII slug ("standard", "elite") the CLI, web API and both GUIs pass across a process
///     boundary — never a display label, which can change independently.
/// </summary>
public readonly record struct EnrolmentPolicyId : IComparable<EnrolmentPolicyId>
{
	public EnrolmentPolicyId(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (!IsValid(value)) {
			throw new ArgumentException(
				$"'{value}' is not a valid policy identifier (expected a lowercase ASCII slug matching [a-z][a-z0-9-]*).",
				nameof(value));
		}

		Value = value;
	}

	public string Value { get; }

	public int CompareTo(EnrolmentPolicyId other) => StringComparer.Ordinal.Compare(Value, other.Value);

	public void Deconstruct(out string value) => value = Value;

	// The zero/default state has a null Value; a strongly-typed-string convention represents that state as
	// the empty string rather than a null ToString.
	public override string ToString() => Value ?? string.Empty;

	public static bool operator <(EnrolmentPolicyId left, EnrolmentPolicyId right) => left.CompareTo(right) < 0;

	public static bool operator <=(EnrolmentPolicyId left, EnrolmentPolicyId right) => left.CompareTo(right) <= 0;

	public static bool operator >(EnrolmentPolicyId left, EnrolmentPolicyId right) => left.CompareTo(right) > 0;

	public static bool operator >=(EnrolmentPolicyId left, EnrolmentPolicyId right) => left.CompareTo(right) >= 0;

	public static bool TryParse(string? value, out EnrolmentPolicyId id)
	{
		if (!IsValid(value)) {
			id = default;
			return false;
		}

		id = new(value!);
		return true;
	}

	public static EnrolmentPolicyId Parse(string value) =>
		TryParse(value, out var id) ? id : throw new FormatException($"'{value}' is not a valid policy identifier.");

	private static bool IsValid(string? value)
	{
		if (string.IsNullOrEmpty(value)) {
			return false;
		}

		if (value[0] is < 'a' or > 'z') {
			return false;
		}

		foreach (var ch in value.AsSpan(1)) {
			if (ch is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')) {
				return false;
			}
		}

		return true;
	}
}
