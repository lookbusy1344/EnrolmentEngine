namespace EnrolmentRules.Domain;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
///     An A-level subject identifier. The shipped catalogue currently defines the built-in static members
///     below, but the type itself is open so a catalogue can introduce a new subject without a recompile.
///     JSON is the raw snake_case string name.
/// </summary>
[JsonConverter(typeof(SubjectJsonConverter))]
public readonly record struct Subject(string Value) : IComparable<Subject>
{
	public static Subject Maths { get; } = new("maths");
	public static Subject FurtherMaths { get; } = new("further_maths");
	public static Subject Physics { get; } = new("physics");
	public static Subject Chemistry { get; } = new("chemistry");
	public static Subject Biology { get; } = new("biology");
	public static Subject EnglishLanguage { get; } = new("english_language");
	public static Subject EnglishLiterature { get; } = new("english_literature");
	public static Subject French { get; } = new("french");
	public static Subject German { get; } = new("german");
	public static Subject PhysicalEducation { get; } = new("physical_education");
	public static Subject ComputerStudies { get; } = new("computer_studies");
	public static Subject History { get; } = new("history");
	public static Subject Music { get; } = new("music");
	public static Subject Art { get; } = new("art");

	public int CompareTo(Subject other) => StringComparer.Ordinal.Compare(Value, other.Value);

	public override string ToString() => Value;

	public static bool operator <(Subject left, Subject right) => left.CompareTo(right) < 0;

	public static bool operator <=(Subject left, Subject right) => left.CompareTo(right) <= 0;

	public static bool operator >(Subject left, Subject right) => left.CompareTo(right) > 0;

	public static bool operator >=(Subject left, Subject right) => left.CompareTo(right) >= 0;

	public static bool TryParse(string? value, out Subject subject)
	{
		if (!IsValid(value)) {
			subject = default;
			return false;
		}

		subject = new(value!);
		return true;
	}

	private static bool IsValid(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) {
			return false;
		}

		var expectLetter = true;
		foreach (var ch in value) {
			if (expectLetter) {
				if (ch is < 'a' or > 'z') {
					return false;
				}

				expectLetter = false;
				continue;
			}

			if (ch == '_') {
				expectLetter = true;
				continue;
			}

			if (ch is < 'a' or > 'z') {
				return false;
			}
		}

		return !expectLetter;
	}
}

public sealed class SubjectJsonConverter : JsonConverter<Subject>
{
	public override Subject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var value = reader.GetString();
		return Subject.TryParse(value, out var subject)
			? subject
			: throw new JsonException($"'{value}' is not a valid subject name.");
	}

	public override void Write(Utf8JsonWriter writer, Subject value, JsonSerializerOptions options) =>
		writer.WriteStringValue(value.Value);
}
