namespace EnrolmentRules.Web.Services;

using Domain;
using Engine;
using Subject = Domain.Subject;

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

	private IEnrolmentEvaluator Evaluator => engine;

	/// <summary>The authoritative A-level list, in catalogue order — the web layer keeps no parallel subject list.</summary>
	public IReadOnlyList<Subject> ALevelSubjects => Evaluator.Catalogue.Subjects;

	/// <summary>The recognised GCSE subject keys <see cref="Domain.StudentValidator" /> accepts.</summary>
	public IReadOnlyList<string> GcseSubjectOptions { get; } = [.. GcseSubjects.Known.Order(StringComparer.Ordinal)];

	public IReadOnlyList<QualificationType> QualificationTypeOptions => CachedQualificationTypeOptions;

	/// <summary>
	///     Subject names a prior qualification can usefully name: every A-level in the catalogue (restudy
	///     bars compare a prior qualification's subject against the A-level being considered) plus every
	///     catalogue <c>entry_equivalents</c> subject (e.g. "applied_science").
	/// </summary>
	public IReadOnlyList<string> PriorQualificationSubjectOptions => [.. BuildPriorQualificationSubjectOptions(Evaluator.Catalogue)];

	/// <summary>Every own-time/veto activity tag referenced anywhere in the catalogue, plus a few illustrative examples.</summary>
	public IReadOnlyList<string> HobbyOptions => [
		.. BuildHobbyOptions(Evaluator.Catalogue).Concat(IllustrativeHobbies).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
	];

	/// <summary>The base selected-A-level cap (<see cref="PolicyThresholds.MaxChosenALevels" />); the high-attainment cap is evaluation-specific.</summary>
	public int ChoiceLimit => Evaluator.Thresholds.MaxChosenALevels;

	public DateOnly DefaultDateOfBirth() => Today().AddYears(-TypicalEnrollmentAgeYears);

	public int DefaultAge() => AgeCalculator.WholeYears(DefaultDateOfBirth(), Today());

	public DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

	private static IEnumerable<string> BuildPriorQualificationSubjectOptions(CatalogueData catalogue) =>
		catalogue.Subjects
			.Select(static subject => subject.Value)
			.Concat(catalogue.Subjects.SelectMany(subject => catalogue.Meta(subject).EntryEquivalents.Select(static e => e.Subject)))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.Ordinal);

	private static IEnumerable<string> BuildHobbyOptions(CatalogueData catalogue) =>
		catalogue.Subjects
			.SelectMany(subject => catalogue.Meta(subject).RequiredActivities.Concat(catalogue.Meta(subject).BlockingActivities))
			.Distinct(StringComparer.Ordinal);
}
