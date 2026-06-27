namespace EnrolmentRules.Domain;

using System.Collections.Frozen;
using System.Text.Json.Nodes;

/// <summary>
///     Per-subject metadata that drives the host-code constraint pass and aggregation (§1.5–1.6):
///     the UCAS tariff <see cref="UcasWeight" /> a green contributes, the subjects this one excludes
///     together with the severity of that exclusion (<see cref="Exclusions" />, a timetable-clash
///     block that can downgrade to amber or red), any required own-time activity prefixes
///     (<see cref="RequiredActivities" />, e.g. <c>plays_</c> for Music), any activity prefixes that
///     <em>bar</em> the subject outright (<see cref="BlockingActivities" /> — the per-subject veto: an
///     incompatible hobby forces red regardless of entry/tier), and the subjects this one depends on
///     (<see cref="Prerequisites" /> — a set of dependency groups, each satisfied by any one of its
///     subjects qualifying this run or being a committed A-level choice; an unmet group downgrades to its
///     own severity, e.g. Further Maths hard-requires Maths).
/// </summary>
public sealed record SubjectMeta(
	int UcasWeight,
	PredictionModel.Coefficients Regression,
	EquatableArray<SubjectExclusion> Exclusions,
	EquatableArray<string> RequiredActivities,
	EquatableArray<string> BlockingActivities,
	EquatableArray<Prerequisite> Prerequisites)
{
	/// <summary>Typed prior qualifications that satisfy or strengthen entry into this subject.</summary>
	public EquatableArray<EntryEquivalent> EntryEquivalents { get; init; } = [];

	/// <summary>Prior qualifications in the same subject that bar re-study.</summary>
	public RestudyBar? RestudyBar { get; init; }
}

/// <summary>A symmetric exclusion edge to another subject with a downgrade severity.</summary>
public readonly record struct SubjectExclusion(Subject Other, Rating Severity);

/// <summary>
///     A prerequisite group: the dependent subject requires <em>any one</em> of <see cref="AnyOf" /> to be
///     satisfied. <see cref="Requires" /> selects how — by qualifying-or-committed (the default) or by a
///     committed choice only. An unmet group downgrades the dependent subject to <see cref="Severity" /> —
///     <see cref="Rating.Red" /> for a hard requirement, <see cref="Rating.Amber" /> for an advisory one.
///     Multiple groups on one subject are AND-ed (each must be satisfied independently); the unmet ones
///     compose by most-severe-wins like every other adjustment.
/// </summary>
public readonly record struct Prerequisite(
	EquatableArray<Subject> AnyOf,
	Rating Severity,
	PrerequisiteSatisfaction Requires = PrerequisiteSatisfaction.Qualifying);

/// <summary>A prior qualification that satisfies this subject's entry policy.</summary>
public readonly record struct EntryEquivalent(string Subject, QualificationType Type, string MinGrade);

/// <summary>A bar on re-studying the subject when the student already holds a qualifying prior qualification.</summary>
public readonly record struct RestudyBar(EquatableArray<QualificationType> Types, Rating Severity);

/// <summary>
///     The validated, immutable catalogue table loaded from <c>data/catalogue.yaml</c>: per-subject
///     <see cref="SubjectMeta" /> plus the derived mutual-exclusion pairs. Construction enforces the two
///     invariants the JSON schema cannot express — every <see cref="Subject" /> has exactly one entry, and
///     every exclusion is symmetric (declared with the same severity on both sides) — so a malformed file
///     fails loud at load rather than silently mis-rating a student downstream.
/// </summary>
public sealed class CatalogueData
{
	private readonly FrozenDictionary<Subject, SubjectMeta> entries;
	private readonly FrozenDictionary<Subject, int> order;

	public CatalogueData(IReadOnlyDictionary<Subject, SubjectMeta> entries)
		: this(entries, [.. entries.Keys], QualificationScale.Current)
	{
	}

	public CatalogueData(IReadOnlyDictionary<Subject, SubjectMeta> entries, IReadOnlyList<Subject> subjects)
		: this(entries, subjects, QualificationScale.Current)
	{
	}

	internal CatalogueData(IReadOnlyDictionary<Subject, SubjectMeta> entries, IReadOnlyList<Subject> subjects, QualificationScale scale)
	{
		this.entries = entries.ToFrozenDictionary();
		Subjects = [.. subjects];
		order = Subjects
			.Select(static (subject, index) => (subject, index))
			.ToFrozenDictionary(static pair => pair.subject, static pair => pair.index);
		Validate(this.entries, scale);

		// Each mutual-exclusion pair once, as an ordered tuple (lower enum value first) so the symmetric
		// SubjectMeta.Exclusions listing is not double-counted. Materialised once: the table is fixed for
		// the process lifetime, so the constraint pass reads a cached array rather than re-running the
		// cross-product.
		ExclusionPairs = [
			.. from subject in Subjects
			from exclusion in this.entries[subject].Exclusions
			let other = exclusion.Other
			where order[subject] < order[other]
			select (subject, other, exclusion.Severity),
		];
	}

	/// <summary>The subjects declared by this catalogue, in file order.</summary>
	public IReadOnlyList<Subject> Subjects { get; }

	/// <summary>Each mutual-exclusion pair once, lower enum value first.</summary>
	public IReadOnlyList<(Subject A, Subject B, Rating Severity)> ExclusionPairs { get; }

	/// <summary>The metadata for <paramref name="subject" /> (guaranteed present by the coverage invariant).</summary>
	public SubjectMeta Meta(Subject subject) => entries[subject];

	// Coverage: every subject has exactly one entry (a missing subject would silently never rate; a
	// duplicate is ambiguous). Symmetry: an exclusion edge A→B must be mirrored B→A with the same severity,
	// because the constraint pass treats clashes as undirected — an asymmetric listing would demote one
	// direction only depending on which subject happened to qualify.
	private static void Validate(FrozenDictionary<Subject, SubjectMeta> entries, QualificationScale scale)
	{
		foreach (var (subject, meta) in entries) {
			foreach (var exclusion in meta.Exclusions) {
				if (!entries.TryGetValue(exclusion.Other, out var otherMeta)) {
					throw new InvalidDataException(
						$"catalogue references undefined subject '{EnumNames.NameOf(exclusion.Other)}' in an exclusion from '{EnumNames.NameOf(subject)}'");
				}

				var mirrored = otherMeta.Exclusions
					.Any(back => back.Other == subject && back.Severity == exclusion.Severity);
				if (!mirrored) {
					throw new InvalidDataException(
						$"catalogue exclusion {EnumNames.NameOf(subject)} → {EnumNames.NameOf(exclusion.Other)} "
						+ $"({EnumNames.NameOf(exclusion.Severity)}) is not declared symmetrically");
				}
			}

			// A prerequisite group with no alternatives can never be satisfied — it would force the dependent
			// subject to its severity unconditionally, which is a configuration mistake, not a policy.
			if (meta.Prerequisites.Any(static group => group.AnyOf.Count == 0)) {
				throw new InvalidDataException(
					$"{EnumNames.NameOf(subject)} has a prerequisite group with no alternatives");
			}

			foreach (var required in meta.Prerequisites.SelectMany(static group => group.AnyOf)) {
				if (!entries.ContainsKey(required)) {
					throw new InvalidDataException(
						$"catalogue references undefined subject '{EnumNames.NameOf(required)}' in prerequisites for '{EnumNames.NameOf(subject)}'");
				}
			}

			foreach (var entryEquivalent in meta.EntryEquivalents) {
				if (string.IsNullOrWhiteSpace(entryEquivalent.Subject)) {
					throw new InvalidDataException(
						$"{EnumNames.NameOf(subject)} has an entry equivalent with a blank subject");
				}

				_ = scale.Ordinal(entryEquivalent.Type, entryEquivalent.MinGrade);
			}

			if (meta.RestudyBar is { } restudyBar) {
				if (restudyBar.Types.Count == 0) {
					throw new InvalidDataException(
						$"{EnumNames.NameOf(subject)} has a restudy bar with no qualification types");
				}
			}
		}
	}
}

/// <summary>
///     The single source of truth for subjects and their cross-subject metadata. Prediction, the workflows
///     and the constraint pass all derive from here, so drift (a subject with no metadata, an exclusion that
///     isn't symmetric) is a load failure, not a silent gap. The table itself lives in
///     <c>data/catalogue.yaml</c>, loaded and validated at startup — editing that file, not this code, is how
///     cross-subject policy changes. The data is hot-swappable: host code may install a freshly loaded table
///     via <see cref="Use" />; absent that, it is read once from the shipped file on first access.
/// </summary>
public static class Catalogue
{
	/// <summary>The catalogue file's location relative to the repository / publish root.</summary>
	public const string DefaultRelativePath = "data/catalogue.yaml";

	// The fallback used when no host has installed a table: load the shipped file once, lazily, so library
	// and test consumers get a valid catalogue with no wiring. The CLI/startup path installs a
	// schema-validated table via Use() before first access, so this lazy never runs in production.
	private static readonly Lazy<CatalogueData> Fallback = new(static () => LoadFromFile(FindDefaultPath()));

	private static volatile CatalogueData? installed;

	/// <summary>
	///     The active catalogue snapshot: installed by the host, or the shipped fallback. This is a
	///     convenience default for zero-wiring callers; constructed engine paths take an explicit
	///     <see cref="CatalogueData" /> and do not consult this property.
	/// </summary>
	public static CatalogueData Current => Active;

	/// <summary>Every catalogue subject (the authoritative subject list, derived from the loaded data).</summary>
	public static IReadOnlyList<Subject> Subjects => Active.Subjects;

	private static CatalogueData Active => installed ?? Fallback.Value;

	/// <summary>Each mutual-exclusion pair once, lower enum value first.</summary>
	public static IReadOnlyList<(Subject A, Subject B, Rating Severity)> ExclusionPairs => Active.ExclusionPairs;

	/// <summary>
	///     Install a loaded catalogue as the active table. This is for single-config convenience hosts and
	///     tests; library consumers should prefer passing a <see cref="CatalogueData" /> explicitly.
	/// </summary>
	public static void Use(CatalogueData data) => installed = data;

	/// <summary>Parse and validate a YAML catalogue document, returning the runtime table.</summary>
	public static CatalogueData Load(string yaml) => Build(YamlConverter.ToJsonNode(yaml));

	/// <summary>Read, parse and validate the catalogue file at <paramref name="path" />.</summary>
	public static CatalogueData LoadFromFile(string path) => Load(File.ReadAllText(path));

	/// <summary>
	///     Project an already-normalized catalogue document (post YAML→JSON, post schema validation) into the
	///     runtime table. Shared with the host-side <c>CatalogueStore</c> so the schema-validated startup path
	///     and the lazy fallback build the table the same way.
	/// </summary>
	public static CatalogueData Build(JsonNode document)
		=> Build(document, QualificationScale.Current);

	/// <summary>
	///     Project an already-normalized catalogue document using an explicit qualification scale.
	///     Host bootstrap paths thread the same scale through prediction and validation so the load-time
	///     invariant check and the runtime engine agree.
	/// </summary>
	public static CatalogueData Build(JsonNode document, QualificationScale scale)
	{
		var entries = new Dictionary<Subject, SubjectMeta>();
		var subjects = new List<Subject>();
		foreach (var entry in CatalogueFile.From(document).Subjects) {
			var meta = new SubjectMeta(
				entry.UcasWeight,
				entry.Regression,
				[.. entry.Exclusions],
				[.. entry.RequiredActivities],
				[.. entry.BlockingActivities],
				[
					.. entry.Prerequisites.Select(static p => new Prerequisite(
						[.. p.AnyOf], p.Severity ?? Rating.Red, p.Requires ?? PrerequisiteSatisfaction.Qualifying)),
				]) {
				EntryEquivalents = [
					.. entry.EntryEquivalents.Select(static equivalent =>
						new EntryEquivalent(equivalent.Subject, equivalent.Type, equivalent.MinGrade)),
				],
				RestudyBar = entry.RestudyBar is { } restudyBar
					? new RestudyBar([.. restudyBar.Types], restudyBar.Severity ?? Rating.Red)
					: null,
			};
			if (!entries.TryAdd(entry.Subject, meta)) {
				throw new InvalidDataException($"catalogue has a duplicate entry for {EnumNames.NameOf(entry.Subject)}");
			}

			subjects.Add(entry.Subject);
		}

		return new(entries, subjects, scale);
	}

	/// <summary>The metadata for <paramref name="subject" />.</summary>
	public static SubjectMeta Meta(Subject subject) => Active.Meta(subject);

	// Resolve the shipped catalogue file: prefer the copy beside the executable (publish output), else walk
	// up from the working directory / base directory to the repository root. Mirrors DfeTransitionMatrix.
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
