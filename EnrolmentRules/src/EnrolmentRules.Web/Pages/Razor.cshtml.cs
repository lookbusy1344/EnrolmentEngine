namespace EnrolmentRules.Web.Pages;

using System.Text.Json;
using Api;
using Domain;
using Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Models;
using Services;
using EquatableArray = Infrastructure.EquatableArray;
using Subject = Domain.Subject;

public sealed class RazorModel(IEnrolmentSessionStore sessionStore, IEnrolmentEngine engine, EnrolmentOptionsService options) : PageModel
{
	[BindProperty] public DateOnly? DateOfBirth { get; set; }

	/// <summary>Whole-years age as of today for the currently displayed <see cref="DateOfBirth" />.</summary>
	public int Age { get; private set; }

	[BindProperty] public List<GcseRowBinding> Gcses { get; set; } = [];

	[BindProperty] public List<PriorQualificationRowBinding> PriorQualifications { get; set; } = [];

	[BindProperty] public List<string> Hobbies { get; set; } = [];

	public EnrolmentResultsViewModel? Results { get; private set; }

	/// <summary>The authoritative A-level list, in catalogue order — the web layer keeps no parallel subject list.</summary>
	public IReadOnlyList<Subject> CatalogueSubjects => options.ALevelSubjects;

	/// <summary>The recognised GCSE subject keys <see cref="Domain.StudentValidator" /> accepts.</summary>
	public IReadOnlyList<string> GcseSubjectOptions => options.GcseSubjectOptions;

	/// <summary>Every grade token defined for each <see cref="QualificationType" />, weakest to strongest — the dependent Grade dropdown's options.</summary>
	public IReadOnlyDictionary<QualificationType, IReadOnlyList<string>> QualificationGradeOptions => options.QualificationGradeOptions;

	/// <summary>
	///     <see cref="QualificationGradeOptions" /> keyed by wire name and serialised for the page's inline
	///     script, which repopulates the Grade dropdown once Type is inferred from the chosen Subject,
	///     without a full postback.
	/// </summary>
	public string QualificationGradeOptionsJson =>
		JsonSerializer.Serialize(EnrolmentOptionsResponseFactory.Create(options).QualificationGrades, EnrolmentApiJsonContext.Default.Options);

	/// <summary>
	///     Subject names a prior qualification can usefully name, one group per exact
	///     <see cref="QualificationType" /> — rendered as <c>&lt;optgroup&gt;</c> sections carrying their
	///     type in a <c>data-type</c> attribute, so the page's inline script can infer Type from whichever
	///     group the chosen subject belongs to instead of the student picking it directly.
	/// </summary>
	public IReadOnlyList<SubjectOptionGroup> PriorQualificationSubjectGroups => options.PriorQualificationSubjectGroups;

	/// <summary>Every own-time/veto activity tag referenced anywhere in the catalogue, plus a few illustrative examples.</summary>
	public IReadOnlyList<string> HobbyOptions => options.HobbyOptions;

	public IReadOnlyList<Subject> ChosenALevels { get; private set; } = [];

	/// <summary>The choices dropped from the basket on this load because they are no longer available.</summary>
	public IReadOnlyList<Subject> EjectedALevels { get; private set; } = [];

	/// <summary>Whether another GCSE row (not <paramref name="excludingIndex" />) already names <paramref name="subjectKey" />.</summary>
	public bool IsGcseSubjectChosenElsewhere(int excludingIndex, string subjectKey) =>
		Gcses.Where((_, idx) => idx != excludingIndex).Any(g => g.Subject == subjectKey);

	public async Task OnGetAsync()
	{
		var session = await PruneStaleChoicesAsync(await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted));
		Bind(session);
		var evaluation = engine.TryExplain(EnrolmentFormMapper.ToStudentInput(session), HttpContext.RequestAborted);
		Results = EnrolmentResultsViewModel.From(evaluation);
	}

	/// <summary>
	///     Drop every committed choice the engine now rates red, persisting the smaller basket before the page
	///     evaluates. A choice was green or amber when the student made it, so a red one means their facts have
	///     since moved (typically lowered GCSEs) — the engine refuses a document that still names it, and the
	///     student would otherwise be stuck on a blank results panel with no way to clear it but "start over".
	///     Ejecting here keeps the session a document the engine will accept.
	/// </summary>
	private async Task<EnrolmentSession> PruneStaleChoicesAsync(EnrolmentSession session)
	{
		var stale = engine.StaleChoices(EnrolmentFormMapper.ToStudentInput(session), HttpContext.RequestAborted);
		if (stale.Count == 0) {
			return session;
		}

		EjectedALevels = stale;
		var pruned = session with { ChosenALevels = EquatableArray.CopyOf(session.ChosenALevels.Except(stale)) };
		await sessionStore.SaveAsync(HttpContext.Session, pruned, HttpContext.RequestAborted);
		return pruned;
	}

	public async Task<IActionResult> OnPostChooseSubjectAsync(string subject)
	{
		if (Subject.TryParse(subject, out var parsed)) {
			// Prune first: CanChoose evaluates the session, and the engine refuses a document still naming a
			// stale choice — leaving one in place would silently block every new choice.
			var session = await PruneStaleChoicesAsync(await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted));
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

	/// <summary>Project a session snapshot onto the form's bound properties for rendering.</summary>
	private void Bind(EnrolmentSession session)
	{
		DateOfBirth = session.DateOfBirth ?? options.DefaultDateOfBirth();
		Age = AgeCalculator.WholeYears(DateOfBirth.Value, options.Today());
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
}
