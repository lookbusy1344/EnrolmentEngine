namespace EnrolmentRules.Domain.Authoring;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

/// <summary>
///     Shared JSON-Schema validation for the data loaders. Compiles each schema once (keyed by the SHA-256
///     of its text — a store reads the same schema file on every load) and evaluates a document against it,
///     throwing a caller-supplied exception carrying the instance-located error detail when it fails.
/// </summary>
public static class SchemaValidator
{
	private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> Cache = new();

	/// <summary>
	///     Validate <paramref name="document" /> against <paramref name="schemaText" />. On failure, throws the
	///     exception produced by <paramref name="onInvalid" /> from the formatted error detail.
	/// </summary>
	public static void Validate(JsonNode document, string schemaText, Func<string, Exception> onInvalid)
	{
		using var doc = JsonDocument.Parse(document.ToJsonString());
		var results = Compile(schemaText).Evaluate(doc.RootElement, new() {
			OutputFormat = OutputFormat.List,
		});
		if (!results.IsValid) {
			throw onInvalid(DescribeErrors(results));
		}
	}

	// The compiled schema for this text, cached across loads. Internal so a test can prove the cache returns
	// one instance per distinct schema text.
	internal static JsonSchema Compile(string schemaText) =>
		Cache.GetOrAdd(Key(schemaText), _ => new(() => JsonSchema.FromText(schemaText))).Value;

	private static string Key(string schemaText) =>
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
