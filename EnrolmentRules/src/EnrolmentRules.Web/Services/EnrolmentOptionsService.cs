namespace EnrolmentRules.Web.Services;

using Domain;
using Engine;
using Subject = Domain.Subject;

/// <summary>
///     One labelled section of a grouped subject picker, keyed by the exact <see cref="QualificationType" />
///     it represents — the client infers Type from whichever group a chosen subject belongs to.
/// </summary>
public readonly record struct SubjectOptionGroup(QualificationType Type, string Label, Infrastructure.EquatableArray<string> Subjects);

/// <summary>
///     The picker/default data the Razor page and the <c>/api/enrolment/options</c> endpoint both need to
///     render a facts form, derived from the same catalogue/validator/scale sources so the two front ends
///     never drift. Recomputed per request/call (registered scoped) rather than cached, so a reloaded
///     engine's catalogue is reflected immediately.
/// </summary>
public sealed class EnrolmentOptionsService(IEnrolmentEngine engine, TimeProvider timeProvider)
{
	/// <summary>
	///     Age assumed for a student who hasn't entered a date of birth yet, used only to pre-fill the date
	///     field with a plausible value (a blank/placeholder date renders oddly dimmed in Safari's native
	///     date picker). Purely a display default: the field remains editable and, like every other fact,
	///     isn't saved until the student submits it.
	/// </summary>
	private const int TypicalEnrollmentAgeYears = 16;

	private static readonly IReadOnlyList<QualificationType> CachedQualificationTypeOptions =
		Array.AsReadOnly(Enum.GetValues<QualificationType>());

	/// <summary>
	///     Illustrative hobby tags with no catalogue backing today — the catalogue currently defines only
	///     the "plays_" prefix and its "plays_trombone" veto (see <see cref="BuildHobbyOptions" />), too thin
	///     a list to be a useful picker on its own. Kept here rather than in the catalogue because they are
	///     placeholders for future policy, not existing rules.
	/// </summary>
	private static readonly string[] IllustrativeHobbies = ["chess_club", "plays_piano", "plays_violin", "sport_football", "reading_"];

	/// <summary>
	///     Illustrative subjects with no catalogue backing today, keyed by <see cref="QualificationType" />
	///     — the catalogue's only real <c>entry_equivalents</c> subject is typed <c>btec_diploma</c>
	///     ("applied_science"), leaving A-level's sibling BTEC Extended Certificate and NVQ groups with
	///     nothing to offer. Kept here rather than in the catalogue for the same reason as
	///     <see cref="IllustrativeHobbies" />: placeholders for future policy, not existing rules.
	/// </summary>
	private static readonly Dictionary<QualificationType, string[]> IllustrativeSubjectsByType = new() {
		[QualificationType.BtecExtendedCertificate] = ["business", "health_and_social_care", "information_technology"],
		[QualificationType.Nvq] = ["construction", "business_administration", "hospitality_and_catering"],
	};

	private IEnrolmentEvaluator Evaluator => engine;

	/// <summary>The authoritative A-level list, in catalogue order — the web layer keeps no parallel subject list.</summary>
	public IReadOnlyList<Subject> ALevelSubjects => Evaluator.Catalogue.Subjects;

	/// <summary>The recognised GCSE subject keys <see cref="Domain.StudentValidator" /> accepts.</summary>
	public IReadOnlyList<string> GcseSubjectOptions { get; } = [.. GcseSubjects.Known.Order(StringComparer.Ordinal)];

	private static readonly Dictionary<QualificationType, string> SubjectGroupLabels = new() {
		[QualificationType.ALevel] = "A-Level subjects",
		[QualificationType.BtecExtendedCertificate] = "BTEC Extended Certificate examples",
		[QualificationType.BtecDiploma] = "BTEC Diploma examples",
		[QualificationType.Nvq] = "NVQ examples",
	};

	public IReadOnlyList<QualificationType> QualificationTypeOptions => CachedQualificationTypeOptions;

	/// <summary>
	///     Every grade token defined for each <see cref="QualificationType" />, weakest to strongest — the
	///     dependent Grade dropdown's options, keyed by the same type each front end already posts.
	/// </summary>
	public IReadOnlyDictionary<QualificationType, IReadOnlyList<string>> QualificationGradeOptions =>
		CachedQualificationTypeOptions.ToDictionary(
			static type => type,
			type => Evaluator.Scale.GradesInOrder(type));

	/// <summary>
	///     Subject names a prior qualification can usefully name, one group per exact
	///     <see cref="QualificationType" />: A-level gets every A-level in the catalogue (restudy bars
	///     compare a prior qualification's subject against the A-level being considered); every other type
	///     gets its catalogue <c>entry_equivalents</c> subjects (e.g. "applied_science" under
	///     <c>BtecDiploma</c>) plus its illustrative examples, if any. The client infers Type from whichever
	///     group the chosen subject belongs to, so the student never picks Type directly.
	/// </summary>
	public IReadOnlyList<SubjectOptionGroup> PriorQualificationSubjectGroups =>
		[.. CachedQualificationTypeOptions.Select(type => BuildSubjectGroup(type, Evaluator.Catalogue))];

	/// <summary>Every own-time/veto activity tag referenced anywhere in the catalogue, plus a few illustrative examples.</summary>
	public IReadOnlyList<string> HobbyOptions => [
		.. BuildHobbyOptions(Evaluator.Catalogue).Concat(IllustrativeHobbies).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
	];

	/// <summary>The base selected-A-level cap (<see cref="PolicyThresholds.MaxChosenALevels" />); the high-attainment cap is evaluation-specific.</summary>
	public int ChoiceLimit => Evaluator.Thresholds.MaxChosenALevels;

	public DateOnly DefaultDateOfBirth() => Today().AddYears(-TypicalEnrollmentAgeYears);

	public int DefaultAge() => AgeCalculator.WholeYears(DefaultDateOfBirth(), Today());

	public DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

	private static SubjectOptionGroup BuildSubjectGroup(QualificationType type, CatalogueData catalogue)
	{
		var realSubjects = type == QualificationType.ALevel
			? catalogue.Subjects.Select(static subject => subject.Value)
			: catalogue.Subjects.SelectMany(subject => catalogue.Meta(subject).EntryEquivalents)
				.Where(equivalent => equivalent.Type == type)
				.Select(static equivalent => equivalent.Subject);
		var illustrativeSubjects = IllustrativeSubjectsByType.GetValueOrDefault(type, []);

		return new(
			type,
			SubjectGroupLabels[type],
			[.. realSubjects.Concat(illustrativeSubjects).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)]);
	}

	private static IEnumerable<string> BuildHobbyOptions(CatalogueData catalogue) =>
		catalogue.Subjects
			.SelectMany(subject => catalogue.Meta(subject).RequiredActivities.Concat(catalogue.Meta(subject).BlockingActivities))
			.Distinct(StringComparer.Ordinal);
}
