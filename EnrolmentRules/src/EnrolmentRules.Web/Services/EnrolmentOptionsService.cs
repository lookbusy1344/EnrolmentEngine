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
///     The picker/default data the <c>/api/enrolment/options</c> endpoint needs to render a facts form,
///     derived from the selected <see cref="EnrolmentPolicy" />'s catalogue/validator/scale. Constructed per
///     request against the caller's resolved policy (never DI-scoped to a single fixed engine), so a
///     Standard and an Elite request in flight at once never share state.
/// </summary>
public sealed class EnrolmentOptionsService(EnrolmentPolicy policy, TimeProvider timeProvider)
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

	private static readonly Dictionary<QualificationType, string> SubjectGroupLabels = new() {
		[QualificationType.ALevel] = "A-Level subjects",
		[QualificationType.BtecExtendedCertificate] = "BTEC Extended Certificate examples",
		[QualificationType.BtecDiploma] = "BTEC Diploma examples",
		[QualificationType.Nvq] = "NVQ examples",
	};

	/// <summary>
	///     The GCSE keys that lead <see cref="GcseSubjectOptions" /> ahead of the alphabet, in display
	///     order — English Language and Maths gate eligibility (§ Accessible tier policy), so the picker
	///     surfaces them first rather than wherever "e" and "m" happen to sort.
	/// </summary>
	private static readonly string[] PinnedGcseSubjects = ["english_language", "maths"];

	private IEnrolmentEvaluator Evaluator => policy.Engine;

	/// <summary>The selected policy's descriptor and the caller's registry snapshot, so the response can name both.</summary>
	public EnrolmentPolicy Policy => policy;

	/// <summary>The authoritative A-level list, in catalogue order — the web layer keeps no parallel subject list.</summary>
	public IReadOnlyList<Subject> ALevelSubjects => Evaluator.Catalogue.Subjects;

	/// <summary>
	///     The recognised GCSE subject keys <see cref="Domain.StudentValidator" /> accepts, with
	///     <see cref="PinnedGcseSubjects" /> first and the remainder alphabetical.
	/// </summary>
	public IReadOnlyList<string> GcseSubjectOptions { get; } =
		[.. PinnedGcseSubjects, .. GcseSubjects.Known.Except(PinnedGcseSubjects).Order(StringComparer.Ordinal)];

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

	/// <summary>The minimum distinct offered choices this policy requires for a final programme (0 when unset — Standard's default).</summary>
	public int MinChoices => Evaluator.Thresholds.MinChosenALevels;

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

	/// <summary>
	///     A catalogue own-time tag is matched with <c>StartsWith</c> (<see cref="Engine.ConstraintPass" />),
	///     so a tag ending in "_" (e.g. Music's "plays_") is a bare wildcard prefix — "any instrument" —
	///     not a real, selectable hobby in its own right. Offering it verbatim would let a student satisfy
	///     the requirement without naming one; <see cref="IllustrativeHobbies" />'s concrete "plays_*"
	///     examples are the real way to satisfy it.
	/// </summary>
	private static IEnumerable<string> BuildHobbyOptions(CatalogueData catalogue) =>
		catalogue.Subjects
				 .SelectMany(subject => catalogue.Meta(subject).RequiredActivities.Concat(catalogue.Meta(subject).BlockingActivities))
				 .Where(static tag => !tag.EndsWith('_'))
				 .Distinct(StringComparer.Ordinal);
}
