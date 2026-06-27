namespace EnrolmentRules.Domain;

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
///     One typed prior qualification on the document boundary: a free-form subject key, a typed
///     qualification family and the raw grade token resolved against the qualification scale.
/// </summary>
public readonly record struct Qualification(string Subject, QualificationType Type, string Grade);

/// <summary>
///     One resolved row in the qualification scale: a qualification type, a grade token, its ordinal
///     within the type and its A-level-points equivalence.
/// </summary>
public readonly record struct QualificationScaleEntry(QualificationType Type, string Grade, int Ordinal, double Equivalence);

/// <summary>
///     The runtime qualification scale used to compare prior qualifications. A missing (type, grade) is a
///     hard failure: lookup drift must stop at the boundary rather than silently returning zero.
/// </summary>
public sealed class QualificationScale
{
	public const string DefaultRelativePath = "data/qualifications.yaml";

	private static readonly Lazy<QualificationScale> Fallback = new(static () => LoadFromFile(FindDefaultPath()));
	private static volatile QualificationScale? installed;

	private readonly FrozenDictionary<QualificationType, FrozenDictionary<string, QualificationScaleEntry>> byType;

	public QualificationScale(IEnumerable<QualificationScaleEntry> entries)
	{
		var grouped = new Dictionary<QualificationType, Dictionary<string, QualificationScaleEntry>>();
		foreach (var entry in entries) {
			ValidateEntry(entry);
			if (!grouped.TryGetValue(entry.Type, out var grades)) {
				grades = [];
				grouped[entry.Type] = grades;
			}

			if (!grades.TryAdd(entry.Grade, entry)) {
				throw new InvalidDataException(
					$"qualification scale has a duplicate entry for {EnumNames.NameOf(entry.Type)} grade '{entry.Grade}'");
			}
		}

		ValidateCoverage(grouped);
		byType = grouped.ToFrozenDictionary(
			static kv => kv.Key,
			static kv => kv.Value.ToFrozenDictionary(static g => g.Key, static g => g.Value));
	}

	/// <summary>The active scale snapshot: installed by the host, or the shipped fallback.</summary>
	public static QualificationScale Current => installed ?? Fallback.Value;

	/// <summary>Install a scale as the active table.</summary>
	public static void Use(QualificationScale scale) => installed = scale;

	/// <summary>Parse a YAML qualification scale document into the runtime table.</summary>
	public static QualificationScale Load(string yaml) => Build(YamlConverter.ToJsonNode(yaml));

	/// <summary>Read and parse the qualification scale file at <paramref name="path" />.</summary>
	public static QualificationScale LoadFromFile(string path) => Load(File.ReadAllText(path));

	/// <summary>
	///     Project an already-normalized qualification-scale document into the runtime table. Shared with
	///     the startup store and the lazy fallback.
	/// </summary>
	public static QualificationScale Build(JsonNode document)
	{
		var file = QualificationScaleFile.From(document);
		return new(file.Qualifications.SelectMany(typeEntry =>
			typeEntry.Grades.Select(grade =>
				new QualificationScaleEntry(typeEntry.Type, grade.Grade, grade.Ordinal, grade.Equivalence))));
	}

	/// <summary>The ordinal of <paramref name="grade" /> within <paramref name="type" />.</summary>
	public int Ordinal(QualificationType type, string grade) => Lookup(type, grade).Ordinal;

	/// <summary>The A-level-points equivalence of <paramref name="grade" /> within <paramref name="type" />.</summary>
	public double Equivalence(QualificationType type, string grade) => Lookup(type, grade).Equivalence;

	/// <summary>Whether <paramref name="qualification" /> satisfies <paramref name="entryEquivalent" />.</summary>
	public bool Satisfies(Qualification qualification, EntryEquivalent entryEquivalent) =>
		qualification.Type == entryEquivalent.Type
		&& string.Equals(qualification.Subject, entryEquivalent.Subject, StringComparison.OrdinalIgnoreCase)
		&& Ordinal(qualification.Type, qualification.Grade) >= Ordinal(entryEquivalent.Type, entryEquivalent.MinGrade);

	private static void ValidateEntry(QualificationScaleEntry entry)
	{
		if (string.IsNullOrWhiteSpace(entry.Grade)) {
			throw new InvalidDataException(
				$"qualification scale entry for {EnumNames.NameOf(entry.Type)} has a blank grade");
		}

		if (entry.Ordinal < 0) {
			throw new InvalidDataException(
				$"qualification scale entry for {EnumNames.NameOf(entry.Type)} grade '{entry.Grade}' has a negative ordinal");
		}

		if (entry.Equivalence is < ALevelGrade.Min or > ALevelGrade.Max) {
			throw new InvalidDataException(
				$"qualification scale entry for {EnumNames.NameOf(entry.Type)} grade '{entry.Grade}' has an out-of-range equivalence");
		}
	}

	private static void ValidateCoverage(Dictionary<QualificationType, Dictionary<string, QualificationScaleEntry>> grouped)
	{
		foreach (var (type, grades) in grouped) {
			if (grades.Count == 0) {
				throw new InvalidDataException(
					$"qualification scale type {EnumNames.NameOf(type)} has no grades");
			}

			var duplicateOrdinal = grades.Values
				.GroupBy(static entry => entry.Ordinal)
				.FirstOrDefault(static group => group.Count() > 1);
			if (duplicateOrdinal is not null) {
				throw new InvalidDataException(
					$"qualification scale type {EnumNames.NameOf(type)} has a duplicate ordinal {duplicateOrdinal.Key}");
			}
		}
	}

	private QualificationScaleEntry Lookup(QualificationType type, string grade)
	{
		if (byType.TryGetValue(type, out var grades) && grades.TryGetValue(grade, out var entry)) {
			return entry;
		}

		throw new InvalidDataException(
			$"unknown qualification {EnumNames.NameOf(type)} grade '{grade}'");
	}

	private static string FindDefaultPath()
	{
		var bundled = Path.Combine(AppContext.BaseDirectory, DefaultRelativePath);
		if (File.Exists(bundled)) {
			return bundled;
		}

		var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
		foreach (var start in starts) {
			for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent) {
				var candidate = Path.Combine(dir.FullName, DefaultRelativePath);
				if (File.Exists(candidate)) {
					return candidate;
				}
			}
		}

		throw new FileNotFoundException($"Could not locate '{DefaultRelativePath}'.");
	}
}

internal sealed record QualificationScaleFile(EquatableArray<QualificationTypeEntry> Qualifications)
{
	public static QualificationScaleFile From(JsonNode node) =>
		node.Deserialize(QualificationScaleJsonContext.Default.QualificationScaleFile)
		?? throw new InvalidDataException("qualification scale document deserialized to null");
}

internal sealed record QualificationTypeEntry
{
	public QualificationType Type { get; init; }

	public EquatableArray<QualificationGradeEntry> Grades { get; init; } = [];
}

internal readonly record struct QualificationGradeEntry(string Grade, int Ordinal, double Equivalence);

/// <summary>Source-generated contract for the qualification scale data file and its runtime DTOs.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(QualificationScaleFile))]
[JsonSerializable(typeof(QualificationTypeEntry))]
[JsonSerializable(typeof(QualificationGradeEntry))]
[JsonSerializable(typeof(QualificationType))]
[JsonSerializable(typeof(string))]
internal sealed partial class QualificationScaleJsonContext : JsonSerializerContext;
