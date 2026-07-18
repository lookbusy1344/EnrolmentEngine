namespace EnrolmentRules.Engine.Authoring;

using System.Collections.Frozen;
using Domain;
using RulesEngine.Models;

/// <summary>
///     Composes one subject's complete, student-facing criteria from the three places policy actually
///     lives: the eligibility workflow (the college-wide gate), that subject's green/amber tier rules,
///     and its <c>catalogue.yaml</c> relationships (the downstream constraint pass). Nothing here is
///     authored prose about a rule — every bullet is derived, so retuning a threshold or editing an entry
///     rule moves the wording with it.
/// </summary>
[CLSCompliant(false)]
public sealed class CriteriaExplainer(
	IReadOnlyList<Workflow> workflows,
	PolicyThresholds thresholds,
	CatalogueData catalogue)
{
	// Acronyms and hyphenation that plain title-casing gets wrong. Cosmetic only: an unlisted type still
	// produces its bullet, just in the generic form, so a new qualification type cannot drop a criterion.
	private static readonly FrozenDictionary<QualificationType, string> QualificationNames =
		new Dictionary<QualificationType, string> {
			[QualificationType.Gcse] = "GCSE",
			[QualificationType.ALevel] = "A-level",
			[QualificationType.BtecExtendedCertificate] = "BTEC Extended Certificate",
			[QualificationType.BtecDiploma] = "BTEC Diploma",
			[QualificationType.Nvq] = "NVQ",
		}.ToFrozenDictionary();

	/// <summary>Describe every criterion that decides <paramref name="subject" />'s rating.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="subject" /> is not in the loaded catalogue.</exception>
	/// <exception cref="CriteriaNarrationException">A rule uses a construct the narrator cannot explain.</exception>
	public SubjectCriteria Describe(Subject subject)
	{
		if (!catalogue.Subjects.Contains(subject)) {
			throw new ArgumentOutOfRangeException(
				nameof(subject), subject, $"'{EnumNames.NameOf(subject)}' is not a subject in the loaded catalogue.");
		}

		var meta = catalogue.Meta(subject);

		return new(
			subject,
			EquatableArray.CopyOf(Eligibility()),
			EquatableArray.CopyOf(Tier(subject, Rating.Green)),
			EquatableArray.CopyOf(Tier(subject, Rating.Amber)),
			EquatableArray.CopyOf([.. Prerequisites(subject, meta), .. Clashes(meta), .. Activities(subject, meta)])) {
			PriorQualifications = EquatableArray.CopyOf([.. EntryEquivalents(subject, meta), .. RestudyBar(subject, meta)]),
		};
	}

	/// <summary>The college-wide gate, in the workflow's declared precedence (English → Maths → pass count).</summary>
	private IReadOnlyList<string> Eligibility() =>
		[.. Rules(RatingEvaluator.EligibilityWorkflow).SelectMany(Narrate)];

	private IReadOnlyList<string> Tier(Subject subject, Rating rating)
	{
		var name = RatingEvaluator.RuleName(subject, rating);
		var rule = Rules(RatingEvaluator.SubjectRatingsWorkflow)
			.FirstOrDefault(candidate => string.Equals(candidate.RuleName, name, StringComparison.Ordinal));

		return rule is null
			? throw new CriteriaNarrationException($"no '{name}' rule exists to describe")
			: Narrate(rule);
	}

	private IReadOnlyList<string> Narrate(Rule rule)
	{
		var locals = (rule.LocalParams ?? [])
			.Where(static param => !string.IsNullOrWhiteSpace(param.Expression))
			.ToDictionary(static param => param.Name, static param => param.Expression!, StringComparer.Ordinal);

		return ExpressionNarrator.Narrate(rule.Expression ?? string.Empty, thresholds, locals);
	}

	private IEnumerable<Rule> Rules(string workflowName) =>
		workflows
			.FirstOrDefault(workflow => string.Equals(workflow.WorkflowName, workflowName, StringComparison.Ordinal))
			?.Rules
		?? throw new CriteriaNarrationException($"workflow '{workflowName}' is not loaded");

	private static IEnumerable<string> Prerequisites(Subject subject, SubjectMeta meta) =>
		meta.Prerequisites.Select(prerequisite => {
			var alternatives = Lists(prerequisite.AnyOf.Select(static other => Naming.Display(EnumNames.NameOf(other))), "or");
			var commitment = prerequisite.Requires == PrerequisiteSatisfaction.Chosen
				? $"You must have chosen {alternatives} as one of your A-levels"
				: $"You must be able to take {alternatives} as well";

			return $"{commitment}. Without it, {Naming.Display(EnumNames.NameOf(subject))} drops to "
				   + $"{EnumNames.NameOf(prerequisite.Severity)}.";
		});

	private static IEnumerable<string> Clashes(SubjectMeta meta) =>
		meta.Exclusions.Select(static exclusion =>
			$"{Naming.Display(EnumNames.NameOf(exclusion.Other))} clashes with this course on the timetable, so taking both "
			+ $"drops it to {EnumNames.NameOf(exclusion.Severity)}.");

	private static IEnumerable<string> Activities(Subject subject, SubjectMeta meta)
	{
		var name = Naming.Display(EnumNames.NameOf(subject));

		foreach (var required in meta.RequiredActivities) {
			yield return $"You need an interest listed as '{required}…' (something you do in your own time). "
						 + $"Without one, {name} drops to {EnumNames.NameOf(Rating.Amber)}.";
		}

		foreach (var blocking in meta.BlockingActivities) {
			yield return $"An interest listed as '{blocking}…' rules {name} out completely "
						 + $"({EnumNames.NameOf(Rating.Red)}), even if you meet everything else.";
		}
	}

	private static IEnumerable<string> EntryEquivalents(Subject subject, SubjectMeta meta) =>
		meta.EntryEquivalents.Select(equivalent =>
			$"{Capitalise(WithArticle(QualificationName(equivalent.Type)))} in {Naming.Display(equivalent.Subject)} at "
			+ $"{GradeName(equivalent.MinGrade)} or above counts instead of the GCSE entry requirement for "
			+ $"{Naming.Display(EnumNames.NameOf(subject))}.");

	private static IEnumerable<string> RestudyBar(Subject subject, SubjectMeta meta)
	{
		if (meta.RestudyBar is not { } bar) {
			yield break;
		}

		var types = Lists(bar.Types.Select(static type => WithArticle(QualificationName(type))), "or");
		yield return $"If you already hold {types} in {Naming.Display(EnumNames.NameOf(subject))}, you cannot study it again "
					 + $"({EnumNames.NameOf(bar.Severity)}).";
	}

	private static string QualificationName(QualificationType type) =>
		QualificationNames.TryGetValue(type, out var name) ? name : Naming.Display(EnumNames.NameOf(type));

	// Letters whose *spoken* name opens on a vowel, so an acronym starting with one takes "an" even though
	// the letter is a consonant: "an NVQ", but "a BTEC".
	private const string VowelSoundLetters = "AEFHILMNORSX";

	private static string WithArticle(string name)
	{
		var vowelSound = name is [var first, var second, ..] && char.IsUpper(first) && char.IsUpper(second)
			? VowelSoundLetters.Contains(first, StringComparison.Ordinal)
			: "AEIOU".Contains(char.ToUpperInvariant(name[0]), StringComparison.Ordinal);

		return vowelSound ? $"an {name}" : $"a {name}";
	}

	private static string Capitalise(string text) =>
		string.Concat(char.ToUpperInvariant(text[0]).ToString(), text[1..]);

	private static string GradeName(string grade) =>
		string.Equals(grade, "a_star", StringComparison.Ordinal) ? "A*" : Naming.Display(grade);

	/// <summary>Join alternatives as a reader would say them: "A", "A or B", "A, B or C".</summary>
	private static string Lists(IEnumerable<string> items, string conjunction)
	{
		var values = items.ToArray();
		return values switch {
			[] => "nothing",
			[var only] => only,
			[var first, var second] => $"{first} {conjunction} {second}",
			_ => $"{string.Join(", ", values[..^1])} {conjunction} {values[^1]}",
		};
	}
}
