namespace EnrolmentRules.Engine.Authoring;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain;
using Json.Schema;

/// <summary>
///     The startup loader for the subject catalogue (the cross-subject constraint policy, §1.5–1.6): reads
///     <c>catalogue.yaml</c>, validates it against <c>catalogue.schema.json</c>, builds the runtime
///     <see cref="CatalogueData" /> (enforcing the coverage / symmetry invariants the schema cannot express)
///     and returns an immutable snapshot for the host to hold. The same fail-loud-at-boot guarantee the workflows get
///     (Reservation 1): a malformed catalogue stops the process rather than silently mis-rating a student.
///     <see cref="Domain.Catalogue" /> can also load the file lazily without schema validation; this is the
///     host path that adds the schema guard.
/// </summary>
public static class CatalogueStore
{
	public const string CatalogueFileName = "catalogue.yaml";
	public const string SchemaFileName = "catalogue.schema.json";

	// Cache the compiled schema per schema text (Lazy so the factory runs once under parallel access),
	// mirroring WorkflowStore — repeated startups/tests reuse one instance.
	private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> SchemaCache = new();

	/// <summary>
	///     Read, schema-validate and build the catalogue from <paramref name="directory" /> (defaults to the
	///     files named beside each other). Throws <see cref="CatalogueException" /> on a structural violation.
	/// </summary>
	public static CatalogueData LoadAndValidate(string directory, string? cataloguePath = null, string? schemaPath = null)
		=> LoadAndValidate(directory, null, cataloguePath, schemaPath);

	/// <summary>
	///     Read, schema-validate and build the catalogue from <paramref name="directory" /> using an explicit
	///     qualification scale. The scale is threaded through the load-time invariants so hosts can boot
	///     multiple catalogues in one process without the check consulting ambient state.
	/// </summary>
	public static CatalogueData LoadAndValidate(
		string directory,
		QualificationScale? scale,
		string? cataloguePath = null,
		string? schemaPath = null)
	{
		cataloguePath ??= Path.Combine(directory, CatalogueFileName);
		schemaPath ??= Path.Combine(directory, SchemaFileName);

		using var catalogueStream = File.OpenRead(cataloguePath);
		using var schemaStream = File.OpenRead(schemaPath);
		return LoadAndValidate(catalogueStream, schemaStream, scale, cataloguePath);
	}

	/// <summary>Read, schema-validate and build the catalogue from arbitrary streams.</summary>
	public static CatalogueData LoadAndValidate(Stream catalogueStream, Stream schemaStream, string? cataloguePath = null)
		=> LoadAndValidate(catalogueStream, schemaStream, null, cataloguePath);

	/// <summary>Read, schema-validate and build the catalogue from arbitrary streams and an explicit scale.</summary>
	public static CatalogueData LoadAndValidate(
		Stream catalogueStream,
		Stream schemaStream,
		QualificationScale? scale,
		string? cataloguePath = null)
	{
		using var catalogueReader = new StreamReader(catalogueStream, Encoding.UTF8, true, 1024, true);
		using var schemaReader = new StreamReader(schemaStream, Encoding.UTF8, true, 1024, true);
		return LoadAndValidate(catalogueReader, schemaReader, scale, cataloguePath);
	}

	/// <summary>Read, schema-validate and build the catalogue from arbitrary text readers.</summary>
	public static CatalogueData LoadAndValidate(TextReader catalogueReader, TextReader schemaReader, string? cataloguePath = null)
		=> LoadAndValidate(catalogueReader, schemaReader, null, cataloguePath);

	/// <summary>Read, schema-validate and build the catalogue from arbitrary text readers and an explicit scale.</summary>
	public static CatalogueData LoadAndValidate(
		TextReader catalogueReader,
		TextReader schemaReader,
		QualificationScale? scale,
		string? cataloguePath = null)
	{
		try {
			var node = YamlConverter.ToJsonNode(catalogueReader.ReadToEnd());
			var schemaText = schemaReader.ReadToEnd();
			var schema = SchemaCache.GetOrAdd(
				SchemaCacheKey(schemaText),
				_ => new(() => JsonSchema.FromText(schemaText))).Value;

			using var doc = JsonDocument.Parse(node.ToJsonString());
			var results = schema.Evaluate(doc.RootElement, new() { OutputFormat = OutputFormat.List });
			if (!results.IsValid) {
				throw new CatalogueException(
					$"Catalogue file '{cataloguePath ?? CatalogueFileName}' failed schema validation: {DescribeErrors(results)}");
			}

			var catalogue = Catalogue.Build(node, scale ?? QualificationScale.Default);
			// Coverage is guarded here (and in Catalogue.LoadFromFile), not in Build, on purpose: Build is also used
			// to construct deliberately partial catalogues (e.g. the open-subject test fixtures), which a
			// full-vocabulary coverage check would wrongly reject. The guard belongs only on the full-load entry points.
			GcseSubjects.ValidateCatalogueCoverage(catalogue.Subjects);
			return catalogue;
		}
		catch (Exception ex) when (ex is InvalidDataException or FormatException) {
			throw new CatalogueException($"Catalogue file '{cataloguePath ?? CatalogueFileName}' is invalid: {ex.Message}", ex);
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

/// <summary>A catalogue file failed schema validation or a load-time invariant at startup (fail loud).</summary>
public sealed class CatalogueException : EnrolmentDataException
{
	public CatalogueException() { }

	public CatalogueException(string message) : base(message) { }

	public CatalogueException(string message, Exception innerException) : base(message, innerException) { }
}
