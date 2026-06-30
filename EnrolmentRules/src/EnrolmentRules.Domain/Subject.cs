namespace EnrolmentRules.Domain;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
///     An A-level subject identifier. The shipped catalogue currently defines the built-in static members
///     below, but the type itself is open so a catalogue can introduce a new subject without a recompile.
///     JSON is the raw snake_case string name.
/// </summary>
[JsonConverter(typeof(SubjectJsonConverter))]
public readonly record struct Subject : IComparable<Subject>
{
	public Subject(string value)
	{
		if (!IsValid(value)) {
			throw new ArgumentException($"'{value}' is not a valid subject name.", nameof(value));
		}

		Value = value;
	}

	public string Value { get; }

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
	public static Subject Economics { get; } = new("economics");
	public static Subject Geography { get; } = new("geography");
	public static Subject Psychology { get; } = new("psychology");
	public static Subject Sociology { get; } = new("sociology");
	public static Subject BusinessStudies { get; } = new("business_studies");
	public static Subject Politics { get; } = new("politics");
	public static Subject ReligiousStudies { get; } = new("religious_studies");
	public static Subject Drama { get; } = new("drama");
	public static Subject MediaStudies { get; } = new("media_studies");
	public static Subject Law { get; } = new("law");
	public static Subject Spanish { get; } = new("spanish");
	public static Subject DesignTechnology { get; } = new("design_technology");

	public int CompareTo(Subject other) => StringComparer.Ordinal.Compare(Value, other.Value);

	public void Deconstruct(out string value) => value = Value;

	// The zero/default state has a null Value; FDG §8 forbids a null ToString, and the strongly-typed-string
	// convention represents that state as the empty string.
	public override string ToString() => Value ?? string.Empty;

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

	public static Subject Parse(string value) =>
		TryParse(value, out var subject)
			? subject
			: throw new FormatException($"'{value}' is not a valid subject name.");

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
