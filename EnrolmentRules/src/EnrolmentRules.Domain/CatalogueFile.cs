namespace EnrolmentRules.Domain;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
///     The on-disk shape of <c>data/catalogue.yaml</c>: a flat list of per-subject entries (rather than a
///     map keyed by the <see cref="Subject" /> enum) so it deserializes reflection-free through the
///     source-generated <see cref="CatalogueJsonContext" />. <see cref="Catalogue" /> projects this into the
///     runtime <see cref="CatalogueData" /> and enforces the coverage / symmetry invariants the schema
///     cannot express.
/// </summary>
internal sealed record CatalogueFile(EquatableArray<CatalogueEntry> Subjects)
{
	/// <summary>Parse a YAML catalogue document (already normalized to a <see cref="JsonNode" />) into the DTO.</summary>
	public static CatalogueFile From(JsonNode node) =>
		node.Deserialize(CatalogueJsonContext.Default.CatalogueFile)
		?? throw new InvalidDataException("catalogue document deserialized to null");
}

/// <summary>One subject's row in <see cref="CatalogueFile" />; arrays default to empty when the key is omitted.</summary>
internal sealed record CatalogueEntry
{
	public Subject Subject { get; init; }
	public int UcasWeight { get; init; }
	public PredictionModel.Coefficients Regression { get; init; }
	public EquatableArray<SubjectExclusion> Exclusions { get; init; } = [];
	public EquatableArray<string> RequiredActivities { get; init; } = [];
	public EquatableArray<string> BlockingActivities { get; init; } = [];
	public EquatableArray<CataloguePrerequisite> Prerequisites { get; init; } = [];
	public EquatableArray<CatalogueEntryEquivalent> EntryEquivalents { get; init; } = [];
	public CatalogueRestudyBar? RestudyBar { get; init; }
}

/// <summary>A prior qualification that satisfies a subject's entry policy in the catalogue file.</summary>
internal sealed record CatalogueEntryEquivalent
{
	public string Subject { get; init; } = string.Empty;

	public QualificationType Type { get; init; }

	public string MinGrade { get; init; } = string.Empty;
}

/// <summary>The on-disk form of a restudy bar: a set of barred qualification types plus an optional severity.</summary>
internal sealed record CatalogueRestudyBar
{
	public EquatableArray<QualificationType> Types { get; init; } = [];

	public Rating? Severity { get; init; }
}

/// <summary>
///     A prerequisite group as written in the file: one or more alternative subjects (<c>any_of</c>) and an
///     optional <c>severity</c> that defaults to <see cref="Rating.Red" /> (a hard requirement) when omitted.
/// </summary>
internal sealed record CataloguePrerequisite
{
	public EquatableArray<Subject> AnyOf { get; init; } = [];

	// Nullable so an omitted key is distinguishable from an explicit one: source generation does not honour a
	// value-type property initializer for a missing key, so the defaults (red, qualifying) are applied on
	// mapping rather than here.
	public Rating? Severity { get; init; }
	public PrerequisiteSatisfaction? Requires { get; init; }
}

/// <summary>
///     Source-generated, reflection-free <see cref="System.Text.Json" /> contract for the catalogue data
///     file. snake_case property names match <c>data/catalogue.yaml</c>; <see cref="Subject" /> and
///     <see cref="Rating" /> carry their own string-name converters. The element/underlying types the
///     <c>EquatableArray</c> converter borrows via <c>GetTypeInfo</c> are registered explicitly so the
///     wrappers stay reflection-free under source-gen.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CatalogueFile))]
[JsonSerializable(typeof(CatalogueEntry))]
[JsonSerializable(typeof(CatalogueEntryEquivalent))]
[JsonSerializable(typeof(CatalogueRestudyBar))]
[JsonSerializable(typeof(SubjectExclusion))]
[JsonSerializable(typeof(CataloguePrerequisite))]
[JsonSerializable(typeof(PredictionModel.Coefficients))]
[JsonSerializable(typeof(Subject))]
[JsonSerializable(typeof(QualificationType))]
[JsonSerializable(typeof(PrerequisiteSatisfaction))]
[JsonSerializable(typeof(string))]
internal sealed partial class CatalogueJsonContext : JsonSerializerContext;
