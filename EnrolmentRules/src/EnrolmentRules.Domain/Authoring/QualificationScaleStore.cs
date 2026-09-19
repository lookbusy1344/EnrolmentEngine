namespace EnrolmentRules.Domain.Authoring;

using System.Text;
using Serialization;

/// <summary>
///     Startup loader for the qualification scale: reads <c>qualifications.yaml</c>, validates it against
///     <c>qualifications.schema.json</c>, enforces full <see cref="QualificationType" /> coverage, and
///     installs the resulting scale as the active lookup table.
/// </summary>
public static class QualificationScaleStore
{
	public const string QualificationsFileName = "qualifications.yaml";
	public const string SchemaFileName = "qualifications.schema.json";

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
		try {
			var node = YamlConverter.ToJsonNode(qualificationsReader.ReadToEnd());
			SchemaValidator.Validate(node, schemaReader.ReadToEnd(), errors => new QualificationScaleException(
				$"qualification scale file '{qualificationsPath ?? QualificationsFileName}' failed schema validation: {errors}"));

			return QualificationScale.RequireCompleteCoverage(QualificationScale.Build(node));
		}
		catch (Exception ex) when (ex is InvalidDataException or FormatException) {
			throw new QualificationScaleException(
				$"qualification scale file '{qualificationsPath ?? QualificationsFileName}' is invalid: {ex.Message}", ex);
		}
	}
}

/// <summary>A qualification scale file failed schema validation or a load-time invariant at startup.</summary>
public sealed class QualificationScaleException : EnrolmentDataException
{
	public QualificationScaleException() { }

	public QualificationScaleException(string message) : base(message) { }

	public QualificationScaleException(string message, Exception innerException) : base(message, innerException) { }
}
