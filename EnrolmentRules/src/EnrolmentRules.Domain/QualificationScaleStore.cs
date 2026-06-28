namespace EnrolmentRules.Domain;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;

/// <summary>
///     Startup loader for the qualification scale: reads <c>qualifications.yaml</c>, validates it against
///     <c>qualifications.schema.json</c> and installs the resulting scale as the active lookup table.
/// </summary>
public static class QualificationScaleStore
{
	public const string QualificationsFileName = "qualifications.yaml";
	public const string SchemaFileName = "qualifications.schema.json";

	private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> SchemaCache = new();

	public static QualificationScale LoadAndValidate(string directory, string? qualificationsPath = null, string? schemaPath = null)
	{
		qualificationsPath ??= Path.Combine(directory, QualificationsFileName);
		schemaPath ??= Path.Combine(directory, SchemaFileName);

		using var qualificationsStream = File.OpenRead(qualificationsPath);
		using var schemaStream = File.OpenRead(schemaPath);
		return LoadAndValidate(qualificationsStream, schemaStream, qualificationsPath);
	}

	public static QualificationScale LoadAndValidate(Stream qualificationsStream, Stream schemaStream, string? qualificationsPath = null)
	{
		using var qualificationsReader = new StreamReader(qualificationsStream, Encoding.UTF8, true, 1024, true);
		using var schemaReader = new StreamReader(schemaStream, Encoding.UTF8, true, 1024, true);
		return LoadAndValidate(qualificationsReader, schemaReader, qualificationsPath);
	}

	public static QualificationScale LoadAndValidate(TextReader qualificationsReader, TextReader schemaReader, string? qualificationsPath = null)
	{
		var node = YamlConverter.ToJsonNode(qualificationsReader.ReadToEnd());
		var schemaText = schemaReader.ReadToEnd();
		var schema = SchemaCache.GetOrAdd(
			SchemaCacheKey(schemaText),
			_ => new(() => JsonSchema.FromText(schemaText))).Value;

		using var doc = JsonDocument.Parse(node.ToJsonString());
		var results = schema.Evaluate(doc.RootElement, new() { OutputFormat = OutputFormat.List });
		if (!results.IsValid) {
			throw new QualificationScaleException(
				$"qualification scale file '{qualificationsPath ?? QualificationsFileName}' failed schema validation: {DescribeErrors(results)}");
		}

		try {
			return QualificationScale.Build(node);
		}
		catch (Exception ex) when (ex is InvalidDataException or FormatException) {
			throw new QualificationScaleException(
				$"qualification scale file '{qualificationsPath ?? QualificationsFileName}' is invalid: {ex.Message}", ex);
		}
	}

	private static string SchemaCacheKey(string schemaText) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemaText)));

	private static string DescribeErrors(EvaluationResults results)
	{
		var messages = (results.Details ?? [])
			.Where(d => d.Errors is { Count: > 0 })
			.SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"));
		var joined = string.Join("; ", messages);
		return joined.Length > 0 ? joined : "schema validation failed (no detailed errors reported)";
	}
}

/// <summary>A qualification scale file failed schema validation or a load-time invariant at startup.</summary>
public sealed class QualificationScaleException : EnrolmentDataException
{
	public QualificationScaleException() { }

	public QualificationScaleException(string message) : base(message) { }

	public QualificationScaleException(string message, Exception innerException) : base(message, innerException) { }
}
