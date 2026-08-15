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

public sealed class RazorModel(
	IEnrolmentSessionStore sessionStore,
	IEnrolmentPolicyRegistry registry,
	TimeProvider timeProvider) : PageModel
{
	private const string PolicySessionKey = "enrolment.policy";

	private EnrolmentOptionsService options = null!;

	[BindProperty] public DateOnly? DateOfBirth { get; set; }

	/// <summary>Whole-years age as of today for the currently displayed <see cref="DateOfBirth" />.</summary>
	public int Age { get; private set; }

	[BindProperty] public List<GcseRowBinding> Gcses { get; set; } = [];

	[BindProperty] public List<PriorQualificationRowBinding> PriorQualifications { get; set; } = [];

	[BindProperty] public List<string> Hobbies { get; set; } = [];

	public EnrolmentResultsViewModel? Results { get; private set; }

	/// <summary>The policy this request resolved to — the URL query value when valid, else the session's last choice, else the registry default.</summary>
	public EnrolmentPolicyDescriptor SelectedPolicy { get; private set; } = null!;

	/// <summary>Every registered policy's descriptor, in registration order — the source for the top switch link, never a hard-coded second list.</summary>
	public IReadOnlyList<EnrolmentPolicyDescriptor> AvailablePolicies => registry.Descriptors;

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
		JsonSerializer.Serialize(
			EnrolmentOptionsResponseFactory.Create(options, registry.Descriptors).QualificationGrades, EnrolmentApiJsonContext.Default.Options);

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

	/// <summary>
	///     <see cref="ChosenALevels" /> paired with each choice's non-destructive status/rating under the
	///     selected policy, so the basket can flag a borderline (amber), unavailable (red) or not-offered
	///     choice rather than showing every entry as equally settled. Empty until <see cref="OnGetAsync" />
	///     has evaluated; the POST handlers redirect, so no rendered page sees it unpopulated.
	/// </summary>
	public IReadOnlyList<BasketEntry> Basket { get; private set; } = [];

	/// <summary>Whether any committed choice is amber, and so needs additional authorisation before enrolment.</summary>
	public bool HasBorderlineChoices => Basket.Any(static entry => entry.IsBorderline);

	/// <summary>
	///     The live GCSE tally (count, total, average) over the entered grades, shown as a scoreboard in the
	///     basket. Its average is the same one the enrolment decision reads. Zeroed until <see cref="OnGetAsync" />
	///     has run; the POST handlers redirect, so no rendered page sees it unpopulated.
	/// </summary>
	public GcseScoreboard Scoreboard { get; private set; }

	/// <summary>Whether another GCSE row (not <paramref name="excludingIndex" />) already names <paramref name="subjectKey" />.</summary>
	public bool IsGcseSubjectChosenElsewhere(int excludingIndex, string subjectKey) =>
		Gcses.Where((_, idx) => idx != excludingIndex).Any(g => g.Subject == subjectKey);

	public async Task<IActionResult> OnGetAsync(string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		var session = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
		Bind(session);
		var student = EnrolmentFormMapper.ToStudentInput(session);
		var comparison = registry.Compare(SelectedPolicy.Id, student, HttpContext.RequestAborted);
		Results = EnrolmentResultsViewModel.From(comparison);
		Basket = BasketEntry.From(Results.Comparison);
		Scoreboard = GcseScoreboard.From(student.ToGcseResults());
		return Page();
	}

	/// <summary>
	///     Resolve the policy this request uses: the <paramref name="requested" /> URL query value when it
	///     names a registered policy, else the session's last selection, else the registry default. Stores
	///     the resolved id back into the session as a convenience for the next request without one. An
	///     invalid/unknown URL value never falls back silently — it redirects to the canonical URL for the
	///     resolved policy instead, so a bad link is visibly corrected rather than quietly served as if the
	///     mistyped policy had been honoured.
	/// </summary>
	private bool ResolvePolicy(string? requested, out IActionResult? redirect)
	{
		if (!string.IsNullOrWhiteSpace(requested)) {
			if (EnrolmentPolicySelector.TryResolve(registry, requested, out var fromUrl)) {
				SelectedPolicy = fromUrl.Descriptor;
				options = new(fromUrl, timeProvider);
				HttpContext.Session.SetString(PolicySessionKey, SelectedPolicy.Id.Value);
				redirect = null;
				return true;
			}

			redirect = RedirectToPage(new
			{
				policy = (string?)null,
			});
			return false;
		}

		var sessionPolicy = HttpContext.Session.GetString(PolicySessionKey);
		var resolved = EnrolmentPolicySelector.TryResolve(registry, sessionPolicy, out var policy)
			? policy
			: registry.GetPolicy(registry.DefaultPolicyId);
		SelectedPolicy = resolved.Descriptor;
		options = new(resolved, timeProvider);
		redirect = null;
		return true;
	}

	public async Task<IActionResult> OnPostChooseSubjectAsync(string subject, string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		if (Subject.TryParse(subject, out var parsed)) {
			var session = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
			if (!session.ChosenALevels.Contains(parsed) && CanChoose(parsed, session)) {
				await sessionStore.SaveAsync(
					HttpContext.Session,
					session with {
						ChosenALevels = EquatableArray.CopyOf([.. session.ChosenALevels, parsed]),
					},
					HttpContext.RequestAborted);
			}
		}

		return RedirectToPage(null, null, new
		{
			policy = SelectedPolicy.Id.Value,
		}, "results-heading");
	}

	public async Task<IActionResult> OnPostRemoveSubjectAsync(string subject, string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		if (Subject.TryParse(subject, out var parsed)) {
			var session = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
			await sessionStore.SaveAsync(
				HttpContext.Session,
				session with {
					ChosenALevels = EquatableArray.CopyOf(session.ChosenALevels.Where(s => s != parsed)),
				},
				HttpContext.RequestAborted);
		}

		return RedirectToPage(null, null, new
		{
			policy = SelectedPolicy.Id.Value,
		}, "results-heading");
	}

	/// <summary>Clears every committed choice from the basket (facts are untouched), then redirects.</summary>
	public async Task<IActionResult> OnPostEmptyBasketAsync(string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		var session = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
		if (session.ChosenALevels.Count > 0) {
			await sessionStore.SaveAsync(
				HttpContext.Session,
				session with {
					ChosenALevels = [],
				},
				HttpContext.RequestAborted);
		}

		return RedirectToPage(null, null, new
		{
			policy = SelectedPolicy.Id.Value,
		}, "results-heading");
	}

	private bool CanChoose(Subject subject, EnrolmentSession session)
	{
		var comparison = registry.Compare(SelectedPolicy.Id, EnrolmentFormMapper.ToStudentInput(session), HttpContext.RequestAborted);
		if (!comparison.Validation.IsValid || comparison.Value is not { Explanation.Eligible: true } value) {
			return false;
		}

		var explanation = value.Explanation.Explanations.SingleOrDefault(explanation => explanation.Subject == subject);
		return explanation is not null && explanation.Rating != Rating.Red;
	}

	/// <summary>Applies the currently posted (bound) facts to the session and redirects.</summary>
	/// <param name="fragment">Anchor to redirect to; a section "Add" button supplies its own section id, the main save button omits it.</param>
	public async Task<IActionResult> OnPostSaveFactsAsync(string? fragment, string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		return await SaveCurrentFactsAsync(fragment ?? "results-heading");
	}

	/// <summary>Removes GCSE row <paramref name="index" /> from the form's current (posted, not-yet-saved) state, then saves.</summary>
	public async Task<IActionResult> OnPostRemoveGcseRowAsync(int index, string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		RemoveAt(Gcses, index);
		return await SaveCurrentFactsAsync("gcse-section");
	}

	/// <summary>Removes prior-qualification row <paramref name="index" /> from the form's current (posted, not-yet-saved) state, then saves.</summary>
	public async Task<IActionResult> OnPostRemoveQualificationRowAsync(int index, string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		RemoveAt(PriorQualifications, index);
		return await SaveCurrentFactsAsync("qualifications-section");
	}

	/// <summary>Removes hobby row <paramref name="index" /> from the form's current (posted, not-yet-saved) state, then saves.</summary>
	public async Task<IActionResult> OnPostRemoveHobbyRowAsync(int index, string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		RemoveAt(Hobbies, index);
		return await SaveCurrentFactsAsync("hobbies-section");
	}

	public async Task<IActionResult> OnPostResetAsync(string? policy)
	{
		if (!ResolvePolicy(policy, out var redirect)) {
			return redirect!;
		}

		await sessionStore.ResetAsync(HttpContext.Session, HttpContext.RequestAborted);
		return RedirectToPage(new
		{
			policy = SelectedPolicy.Id.Value,
		});
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
	private async Task<IActionResult> SaveCurrentFactsAsync(string fragment)
	{
		var current = await sessionStore.LoadAsync(HttpContext.Session, HttpContext.RequestAborted);
		var input = new SaveFactsInput(
			DateOfBirth,
			EquatableArray.CopyOf(Gcses.Where(static row => row is not null).Select(static row => row.ToRow())),
			EquatableArray.CopyOf(PriorQualifications.Where(static row => row is not null).Select(static row => row.ToRow())),
			EquatableArray.CopyOf(Hobbies.Where(static hobby => hobby is not null)));

		await sessionStore.SaveAsync(HttpContext.Session, EnrolmentFormMapper.Apply(input, current), HttpContext.RequestAborted);
		return RedirectToPage(null, null, new
		{
			policy = SelectedPolicy.Id.Value,
		}, fragment);
	}
}
