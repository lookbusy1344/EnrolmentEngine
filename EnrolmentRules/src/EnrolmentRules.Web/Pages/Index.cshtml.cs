namespace EnrolmentRules.Web.Pages;

using Domain;
using Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Models;
using Services;
using EquatableArray = Infrastructure.EquatableArray;
using Subject = Domain.Subject;

public sealed class IndexModel(IEnrolmentSessionStore sessionStore, IEnrolmentEngine engine, TimeProvider timeProvider) : PageModel
{
	/// <summary>
	///     Age assumed for a student who hasn't entered a date of birth yet, used only to pre-fill the date
	///     field with a plausible value (a blank/placeholder date renders oddly dimmed in Safari's native
	///     date picker — see the field's own comment in Index.cshtml). Purely a display default: the field
	///     remains editable and, like every other fact, isn't saved until the student submits the form.
	/// </summary>
	private const int TypicalEnrollmentAgeYears = 16;

	private static readonly IReadOnlyList<QualificationType> CachedQualificationTypeOptions =
		Array.AsReadOnly(Enum.GetValues<QualificationType>());

	/// <summary>
	///     Illustrative hobby tags with no catalogue backing today — the catalogue currently defines only
	///     the "plays_" prefix and its "plays_trombone" veto (see <see cref="BuildHobbyOptions" />), too
	///     thin a list to be a useful picker on its own. Kept here rather than in the catalogue because
	///     they are placeholders for future policy, not existing rules.
	/// </summary>
	private static readonly string[] IllustrativeHobbies = ["chess_club", "plays_piano", "plays_violin", "sport_football", "reading_"];

	[BindProperty] public DateOnly? DateOfBirth { get; set; }

	/// <summary>Whole-years age as of today for the currently displayed <see cref="DateOfBirth" />.</summary>
	public int Age { get; private set; }

	[BindProperty] public List<GcseRowBinding> Gcses { get; set; } = [];

	[BindProperty] public List<PriorQualificationRowBinding> PriorQualifications { get; set; } = [];

	[BindProperty] public List<string> Hobbies { get; set; } = [];

	public EnrolmentResultsViewModel? Results { get; private set; }

	/// <summary>The authoritative A-level list, in catalogue order — the web layer keeps no parallel subject list.</summary>
	public IReadOnlyList<Subject> CatalogueSubjects => ((IEnrolmentEvaluator)engine).Catalogue.Subjects;

	/// <summary>The recognised GCSE subject keys <see cref="Domain.StudentValidator" /> accepts.</summary>
	public IReadOnlyList<string> GcseSubjectOptions { get; } = [.. GcseSubjects.Known.Order(StringComparer.Ordinal)];

	public IReadOnlyList<QualificationType> QualificationTypeOptions => CachedQualificationTypeOptions;

	/// <summary>
	///     Subject names a prior qualification can usefully name: every A-level in the catalogue (restudy
	///     bars compare a prior qualification's subject against the A-level being considered) plus every
	///     catalogue <c>entry_equivalents</c> subject (e.g. "applied_science").
	/// </summary>
	public IReadOnlyList<string> PriorQualificationSubjectOptions { get; } =
		[.. BuildPriorQualificationSubjectOptions(((IEnrolmentEvaluator)engine).Catalogue)];

	/// <summary>Every own-time/veto activity tag referenced anywhere in the catalogue, plus a few illustrative examples.</summary>
	public IReadOnlyList<string> HobbyOptions { get; } = [
		.. BuildHobbyOptions(((IEnrolmentEvaluator)engine).Catalogue).Concat(IllustrativeHobbies).Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal),
	];

	public IReadOnlyList<Subject> ChosenALevels { get; private set; } = [];

	public async Task OnGetAsync()
	{
		var session = await LoadFromSessionAsync();
		var evaluation = engine.TryExplain(EnrolmentFormMapper.ToStudentInput(session), HttpContext.RequestAborted);
		Results = EnrolmentResultsViewModel.From(evaluation);
	}

	public async Task<IActionResult> OnPostChooseSubjectAsync(string subject)
	{
		if (Subject.TryParse(subject, out var parsed)) {
			var session = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
			if (!session.ChosenALevels.Contains(parsed) && CanChoose(parsed, session)) {
				await sessionStore.SaveAsync(
					HttpContext.Session,
					session with { ChosenALevels = EquatableArray.CopyOf([.. session.ChosenALevels, parsed]) },
					HttpContext.RequestAborted);
			}
		}

		return RedirectToPage(null, null, "results-heading");
	}

	public async Task<IActionResult> OnPostRemoveSubjectAsync(string subject)
	{
		if (Subject.TryParse(subject, out var parsed)) {
			var session = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
			await sessionStore.SaveAsync(
				HttpContext.Session,
				session with { ChosenALevels = EquatableArray.CopyOf(session.ChosenALevels.Where(s => s != parsed)) },
				HttpContext.RequestAborted);
		}

		return RedirectToPage(null, null, "results-heading");
	}

	private bool CanChoose(Subject subject, EnrolmentSession session)
	{
		var evaluation = engine.TryExplain(EnrolmentFormMapper.ToStudentInput(session), HttpContext.RequestAborted);
		if (!evaluation.Validation.IsValid || evaluation.Value is not { Eligible: true } result) {
			return false;
		}

		var explanation = result.Explanations.SingleOrDefault(explanation => explanation.Subject == subject);
		return explanation is not null && explanation.Rating != Rating.Red;
	}

	/// <summary>Applies the currently posted (bound) facts to the session and redirects.</summary>
	/// <param name="fragment">Anchor to redirect to; a section "Add" button supplies its own section id, the main save button omits it.</param>
	public Task<RedirectToPageResult> OnPostSaveFactsAsync(string? fragment) => SaveCurrentFactsAsync(fragment ?? "results-heading");

	/// <summary>Removes GCSE row <paramref name="index" /> from the form's current (posted, not-yet-saved) state, then saves.</summary>
	public Task<RedirectToPageResult> OnPostRemoveGcseRowAsync(int index)
	{
		RemoveAt(Gcses, index);
		return SaveCurrentFactsAsync("gcse-section");
	}

	/// <summary>Removes prior-qualification row <paramref name="index" /> from the form's current (posted, not-yet-saved) state, then saves.</summary>
	public Task<RedirectToPageResult> OnPostRemoveQualificationRowAsync(int index)
	{
		RemoveAt(PriorQualifications, index);
		return SaveCurrentFactsAsync("qualifications-section");
	}

	/// <summary>Removes hobby row <paramref name="index" /> from the form's current (posted, not-yet-saved) state, then saves.</summary>
	public Task<RedirectToPageResult> OnPostRemoveHobbyRowAsync(int index)
	{
		RemoveAt(Hobbies, index);
		return SaveCurrentFactsAsync("hobbies-section");
	}

	public async Task<IActionResult> OnPostResetAsync()
	{
		await sessionStore.ResetAsync(HttpContext.Session, HttpContext.RequestAborted);
		return RedirectToPage();
	}

	private async Task<EnrolmentSession> LoadFromSessionAsync()
	{
		var session = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
		DateOfBirth = session.DateOfBirth ?? DefaultDateOfBirth();
		Age = AgeCalculator.WholeYears(DateOfBirth.Value, Today());
		Gcses = WithTrailingBlankRow(
			[.. session.Gcses.Select(GcseRowBinding.FromRow)],
			static row => row.ToRow().IsEmpty,
			static () => new());
		PriorQualifications = WithTrailingBlankRow(
			[.. session.PriorQualifications.Select(PriorQualificationRowBinding.FromRow)],
			static row => row.ToRow().IsEmpty,
			static () => new());
		Hobbies = WithTrailingBlankRow([.. session.Hobbies], static hobby => string.IsNullOrWhiteSpace(hobby), static () => string.Empty);
		ChosenALevels = [.. session.ChosenALevels];
		return session;
	}

	/// <summary>
	///     Always leaves one blank row at the end of a repeatable-row list, so the form has room to add another entry without a dedicated "add row"
	///     post.
	/// </summary>
	private static List<T> WithTrailingBlankRow<T>(List<T> rows, Func<T, bool> isEmpty, Func<T> blank)
	{
		if (rows.Count == 0 || !isEmpty(rows[^1])) {
			rows.Add(blank());
		}

		return rows;
	}

	private DateOnly DefaultDateOfBirth() => Today().AddYears(-TypicalEnrollmentAgeYears);

	private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

	private static void RemoveAt<T>(List<T> rows, int index)
	{
		if (index >= 0 && index < rows.Count) {
			rows.RemoveAt(index);
		}
	}

	/// <summary>Applies the form's currently posted (bound) facts to the session and redirects — the shared tail of every facts-editing handler.</summary>
	/// <param name="fragment">Anchor id to redirect to, so the reload lands back near the row the user was editing instead of the page top.</param>
	private async Task<RedirectToPageResult> SaveCurrentFactsAsync(string fragment)
	{
		var current = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
		var input = new SaveFactsInput(
			DateOfBirth,
			EquatableArray.CopyOf(Gcses.Where(static row => row is not null).Select(static row => row.ToRow())),
			EquatableArray.CopyOf(PriorQualifications.Where(static row => row is not null).Select(static row => row.ToRow())),
			EquatableArray.CopyOf(Hobbies.Where(static hobby => hobby is not null)));

		await sessionStore.SaveAsync(HttpContext.Session, EnrolmentFormMapper.Apply(input, current), HttpContext.RequestAborted);
		return RedirectToPage(null, null, fragment);
	}

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
