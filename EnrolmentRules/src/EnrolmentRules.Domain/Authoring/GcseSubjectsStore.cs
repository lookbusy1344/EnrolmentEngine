namespace EnrolmentRules.Domain.Authoring;

using System.Text;
using Serialization;

/// <summary>
///     Startup loader for the GCSE vocabulary: reads <c>gcse-subjects.yaml</c>, validates it against
///     <c>gcse-subjects.schema.json</c>, and installs the resulting vocabulary as the active lookup set.
/// </summary>
public static class GcseSubjectsStore
{
	public const string GcseSubjectsFileName = "gcse-subjects.yaml";
	public const string SchemaFileName = "gcse-subjects.schema.json";

	public static GcseVocabulary LoadAndValidate(string directory, string? gcseSubjectsPath = null, string? schemaPath = null)
	{
		gcseSubjectsPath ??= Path.Combine(directory, GcseSubjectsFileName);
		schemaPath ??= Path.Combine(directory, SchemaFileName);

		using var gcseSubjectsStream = File.OpenRead(gcseSubjectsPath);
		using var schemaStream = File.OpenRead(schemaPath);
		return LoadAndValidate(gcseSubjectsStream, schemaStream, gcseSubjectsPath);
	}

	public static GcseVocabulary LoadAndValidate(Stream gcseSubjectsStream, Stream schemaStream, string? gcseSubjectsPath = null)
	{
		using var gcseSubjectsReader = new StreamReader(gcseSubjectsStream, Encoding.UTF8, true, 1024, true);
		using var schemaReader = new StreamReader(schemaStream, Encoding.UTF8, true, 1024, true);
		return LoadAndValidate(gcseSubjectsReader, schemaReader, gcseSubjectsPath);
	}

	public static GcseVocabulary LoadAndValidate(TextReader gcseSubjectsReader, TextReader schemaReader, string? gcseSubjectsPath = null)
	{
		try {
			var node = YamlConverter.ToJsonNode(gcseSubjectsReader.ReadToEnd());
			SchemaValidator.Validate(node, schemaReader.ReadToEnd(), errors => new GcseSubjectsException(
				$"GCSE vocabulary file '{gcseSubjectsPath ?? GcseSubjectsFileName}' failed schema validation: {errors}"));

			return GcseVocabulary.Build(node);
		}
		catch (Exception ex) when (ex is InvalidDataException or FormatException) {
			throw new GcseSubjectsException(
				$"GCSE vocabulary file '{gcseSubjectsPath ?? GcseSubjectsFileName}' is invalid: {ex.Message}", ex);
		}
	}
}

/// <summary>A GCSE vocabulary file failed schema validation or a load-time invariant at startup.</summary>
public sealed class GcseSubjectsException : EnrolmentDataException
{
	public GcseSubjectsException() { }

	public GcseSubjectsException(string message) : base(message) { }

	public GcseSubjectsException(string message, Exception innerException) : base(message, innerException) { }
}
