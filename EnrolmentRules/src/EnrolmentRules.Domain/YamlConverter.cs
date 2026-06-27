namespace EnrolmentRules.Domain;

using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

/// <summary>
///     Reflection-free YAML→JSON normalization shared by the workflow loader (rules-as-data) and the CLI
///     input boundary (student documents). Parses a single-document YAML stream into the same
///     <see cref="JsonNode" /> shape <c>System.Text.Json</c> produces, so an existing source-generated
///     contract can deserialize a YAML document with no extra reflection. Unquoted plain scalars are coerced
///     to <c>null</c>/<c>bool</c>/integer/floating-point (YAML 1.1 plain-scalar conventions); quoted scalars
///     stay strings. Malformed input surfaces as <see cref="FormatException" /> for the caller to map onto
///     its own error contract.
/// </summary>
public static class YamlConverter
{
	/// <summary>Convert a single-document YAML string to a <see cref="JsonNode" /> tree.</summary>
	public static JsonNode ToJsonNode(string yaml)
	{
		var stream = new YamlStream();
		using var reader = new StringReader(yaml);
		try {
			stream.Load(reader);
		}
		catch (YamlException ex) {
			throw new FormatException($"could not parse YAML: {ex.Message}", ex);
		}

		if (stream.Documents.Count != 1) {
			throw new FormatException($"YAML must contain exactly one document (found {stream.Documents.Count})");
		}

		var root = stream.Documents[0].RootNode
				   ?? throw new FormatException("YAML document had no root node");
		return ConvertNode(root)
			   ?? throw new FormatException("YAML document normalized to null");
	}

	private static JsonNode? ConvertNode(YamlNode node) =>
		node switch {
			YamlScalarNode scalar => ConvertScalar(scalar),
			YamlSequenceNode sequence => new JsonArray([.. sequence.Children.Select(ConvertNode)]),
			YamlMappingNode mapping => ConvertMapping(mapping),
			_ => throw new FormatException($"unsupported YAML node type '{node.GetType().Name}'"),
		};

	private static JsonObject ConvertMapping(YamlMappingNode mapping)
	{
		var json = new JsonObject();
		foreach (var (keyNode, valueNode) in mapping.Children) {
			if (keyNode is not YamlScalarNode { Value: { } key } || string.IsNullOrWhiteSpace(key)) {
				throw new FormatException("YAML mapping keys must be non-empty scalars");
			}

			json[key] = ConvertNode(valueNode);
		}

		return json;
	}

	private static JsonValue? ConvertScalar(YamlScalarNode scalar)
	{
		if (scalar.Value is null) {
			return null;
		}

		// A quoted scalar is always a string, even if it looks like a number or bool.
		if (scalar.Style is not ScalarStyle.Any and not ScalarStyle.Plain) {
			return JsonValue.Create(scalar.Value);
		}

		if (string.Equals(scalar.Value, "null", StringComparison.OrdinalIgnoreCase) || scalar.Value == "~") {
			return null;
		}

		if (bool.TryParse(scalar.Value, out var boolean)) {
			return JsonValue.Create(boolean);
		}

		if (long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) {
			return JsonValue.Create(integer);
		}

		if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating)) {
			return JsonValue.Create(floating);
		}

		return JsonValue.Create(scalar.Value);
	}
}
