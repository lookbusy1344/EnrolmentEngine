namespace EnrolmentRules.Domain;

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Serialization;

/// <summary>
///     The recognised GCSE subject keys (§1.1). This is the GCSE-side vocabulary, distinct from the
///     A-level <see cref="Subject" /> type: it carries <c>english_language</c> (a GCSE that gates
///     eligibility and the English subject entry rules) and omits <c>further_maths</c> (an A-level
///     with no GCSE of its own). It is the single source of truth the input validator checks an
///     incoming document's GCSE keys against, so an unknown key is rejected at the boundary rather
///     than silently treated as "not taken". The table lives in <c>data/gcse-subjects.yaml</c>, loaded
///     and validated at startup — editing that file, not this code, is how the vocabulary changes.
///     <see cref="Default" /> is an immutable, lazily-loaded snapshot of the shipped file for
///     zero-wiring callers; there is no installer and no swap. A constructed engine threads its own
///     explicit <see cref="GcseVocabulary" /> and never consults it.
/// </summary>
public sealed class GcseVocabulary
{
	public const string DefaultRelativePath = "data/gcse-subjects.yaml";

	private static readonly Lazy<GcseVocabulary> Shipped = new(static () => LoadFromFile(FindDefaultPath()));

	public GcseVocabulary(IEnumerable<string> subjects)
	{
		var known = new HashSet<string>(StringComparer.Ordinal);
		foreach (var subject in subjects) {
			if (string.IsNullOrWhiteSpace(subject)) {
				throw new InvalidDataException("GCSE vocabulary has a blank subject key.");
			}

			if (!known.Add(subject)) {
				throw new InvalidDataException($"GCSE vocabulary has a duplicate entry for '{subject}'.");
			}
		}

		Known = known.ToFrozenSet(StringComparer.Ordinal);
	}

	/// <summary>
	///     The immutable shipped GCSE vocabulary, read once from <c>data/gcse-subjects.yaml</c> on first
	///     access. A read-only convenience default for the zero-wiring overloads; constructed engine
	///     paths take an explicit <see cref="GcseVocabulary" /> and do not consult it.
	/// </summary>
	public static GcseVocabulary Default => Shipped.Value;

	/// <summary>The recognised GCSE subject keys (snake_case, matching the document and workflow lambdas).</summary>
	public IReadOnlySet<string> Known { get; }

	/// <summary>Whether <paramref name="subject" /> is a recognised GCSE subject key.</summary>
	public bool IsKnown(string subject) => Known.Contains(subject);

	/// <summary>Parse a YAML GCSE vocabulary document into the runtime table.</summary>
	public static GcseVocabulary Load(string yaml) => Build(YamlConverter.ToJsonNode(yaml));

	/// <summary>Read and parse the GCSE vocabulary file at <paramref name="path" />.</summary>
	public static GcseVocabulary LoadFromFile(string path) => Load(File.ReadAllText(path));

	/// <summary>
	///     Project an already-normalized GCSE vocabulary document (post YAML→JSON, post schema
	///     validation) into the runtime table. Shared with the host-side <c>GcseSubjectsStore</c> so the
	///     schema-validated startup path and the lazy fallback build the table the same way.
	/// </summary>
	public static GcseVocabulary Build(JsonNode document) => new(GcseVocabularyFile.From(document).Subjects);

	private static string FindDefaultPath() => ShippedLayout.Locate(DefaultRelativePath);
}

internal sealed record GcseVocabularyFile(EquatableArray<string> Subjects)
{
	public static GcseVocabularyFile From(JsonNode node) =>
		node.Deserialize(GcseVocabularyJsonContext.Default.GcseVocabularyFile)
		?? throw new InvalidDataException("GCSE vocabulary document deserialized to null.");
}

/// <summary>Source-generated contract for the GCSE vocabulary data file and its runtime DTO.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GcseVocabularyFile))]
[JsonSerializable(typeof(string))]
internal sealed partial class GcseVocabularyJsonContext : JsonSerializerContext;
