namespace EnrolmentRules.Domain.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

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
