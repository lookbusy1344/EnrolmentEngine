#pragma warning disable CS3019
namespace EnrolmentRules.Engine;

using Domain;

/// <summary>
///     The cross-subject constraint pass (§1.5–1.6) — the constraint half of the problem that cannot live
///     in the engine because a rule can't read sibling rules' results (Reservation 2). A pure function
///     over the collected base <see cref="SubjectRating" />s producing the <see cref="Adjustment" />
///     trail: an unmet prerequisite, a red-severity exclusion or a per-subject veto can produce red, mutual
///     exclusion and own-time can downgrade to amber, and chosen A-levels can trigger the same downgrade
///     severity against a prior choice. Every adjustment only downgrades, so they commute within the pass and
///     compose by most-severe-wins — the acyclic/monotone property that justifies a stateless engine
///     over RETE.
/// </summary>
internal static class ConstraintPass
{
	public const string OwnTimeReason = "requires own-time practice — authorisation required";
	public const string VetoReasonPrefix = "Barred — incompatible activity: ";
	public const string RestudyBarReasonPrefix = "Barred — already holds a level-3 qualification in ";
	private const string MutualExclusionAmberReasonPrefix = "Mutual exclusion with ";
	private const string MutualExclusionAmberReasonSuffix = " — authorisation required";
	private const string MutualExclusionRedReasonPrefix = "Mutual exclusion with ";
	private const string MutualExclusionRedReasonSuffix = " — not permitted";
	private const string PriorChoiceExclusionReasonPrefix = "Cannot be combined with chosen ";
	private const string PrerequisiteReasonSuffix = " prerequisite not met";
	private const string PrerequisiteReasonAlternativeSeparator = " or ";

	/// <summary>The canonical reason for the (single-subject, hard) Maths prerequisite Further Maths carries.</summary>
	public static readonly string MathsPrerequisiteReason =
		EnumNames.NameOf(Subject.Maths) + PrerequisiteReasonSuffix;

	/// <summary>
	///     Collect every downgrade for one student in two phases. The non-prerequisite constraints (mutual and
	///     prior-choice exclusion, own-time, veto, restudy bar) read only the <em>base</em> ratings; a
	///     prerequisite then reads the ratings <em>after</em> those downgrades, so a dependency a sibling
	///     constraint drove to red no longer satisfies a dependent's requirement. Because none of the
	///     non-prerequisite constraints read prerequisite output, this is a fixed two-phase order — not a
	///     fixpoint iteration — and it stays acyclic. Application still commutes: <see cref="Apply" /> folds
	///     the whole trail by most-severe-wins regardless of order; only this evaluation order is fixed.
	/// </summary>
	public static IReadOnlyList<Adjustment> Evaluate(
		IReadOnlyList<SubjectRating> ratings,
		StudentProfile profile,
		CatalogueData catalogue)
	{
		var baseRating = ratings.ToDictionary(static r => r.Subject, static r => r.Rating);

		IReadOnlyList<Adjustment> nonPrerequisite = [
			.. MutualExclusions(baseRating, catalogue),
			.. PriorChoiceExclusions(baseRating, profile.ChosenALevels, catalogue),
			.. OwnTime(baseRating, profile.Hobbies, catalogue),
			.. Vetoes(baseRating, profile.Hobbies, catalogue),
			.. RestudyBars(baseRating, profile.PriorQualifications, catalogue),
		];

		// Prerequisites see the ratings after the phase-one downgrades. A prerequisite chained on another
		// prerequisite's target is still resolved against these phase-one ratings — deep prerequisite chains
		// would need ordered resolution, which the catalogue does not currently exercise.
		var adjustedRating = Apply(ratings, nonPrerequisite).ToDictionary(static r => r.Subject, static r => r.Rating);
		return [
			.. Prerequisites(baseRating, adjustedRating, profile.ChosenALevels, catalogue),
			.. nonPrerequisite,
		];
	}

	/// <summary>
	///     Apply the adjustments to the base ratings: each subject's final rating is the most severe of its
	///     base and any adjustment, and a downgraded subject carries the deciding adjustment's reason.
	/// </summary>
	public static IReadOnlyList<SubjectRating> Apply(
		IReadOnlyList<SubjectRating> ratings,
		IReadOnlyList<Adjustment> adjustments)
	{
		// The winner within a subject is the most severe adjustment, ties broken by AdjustmentKind precedence
		// (the typed discriminator, not the reason text). Apply owns the monotonicity invariant and defensively
		// ignores a winner less severe than the base. That keeps same-severity reason replacement (own-time
		// amber→amber, veto/restudy red→red) without allowing an invalid adjustment to relabel or upgrade a
		// more-severe base.
		var worst = adjustments
			.GroupBy(static a => a.Subject)
			.ToDictionary(static g => g.Key, static g => g.MaxBy(static a => ((int)a.To, (int)a.Kind))!);

		return [
			.. ratings.Select(r =>
				worst.TryGetValue(r.Subject, out var adjustment) && adjustment.To >= r.Rating
					? r with { Rating = adjustment.To, Reason = adjustment.Reason }
					: r),
		];
	}

	/// <summary>A subject "qualifies" if it has a green or amber base rating (i.e. is not red / not absent).</summary>
	private static bool Qualifies(Dictionary<Subject, Rating> ratings, Subject subject) =>
		ratings.TryGetValue(subject, out var rating) && rating != Rating.Red;

	private static bool IsGreen(Dictionary<Subject, Rating> ratings, Subject subject) =>
		ratings.GetValueOrDefault(subject, Rating.Red) == Rating.Green;

	// Prerequisite (→ amber or red): for each qualifying subject, every dependency group must be satisfied —
	// a group is met when any one of its alternatives qualifies in this run or is a committed A-level (a
	// committed choice is at least as strong as a qualifying rating). Availability reads the phase-one
	// adjusted ratings, so a dependency vetoed / restudy-barred / excluded to red no longer counts as
	// qualifying; the dependent selection and the adjustment's From still read the base ratings. Each unmet
	// group emits a downgrade to its own severity; most-severe-wins composes them in Apply. Driven by the
	// Catalogue's per-subject Prerequisites table, not hardcoded — editing the table is how policy changes.
	private static IEnumerable<Adjustment> Prerequisites(
		Dictionary<Subject, Rating> baseRatings,
		Dictionary<Subject, Rating> adjustedRatings,
		IReadOnlyList<Subject> chosenALevels,
		CatalogueData catalogue)
	{
		bool Available(Subject required, PrerequisiteSatisfaction requires) =>
			IsPrerequisiteAvailable(required, requires, r => Qualifies(adjustedRatings, r), chosenALevels);

		foreach (var subject in catalogue.Subjects.Where(subject => catalogue.Meta(subject).Prerequisites.Count > 0)) {
			if (!Qualifies(baseRatings, subject)) {
				continue;
			}

			foreach (var adjustment in PrerequisiteAdjustments(
						 subject, baseRatings[subject], catalogue.Meta(subject).Prerequisites, Available)) {
				yield return adjustment;
			}
		}
	}

	/// <summary>
	///     Whether <paramref name="required" /> satisfies a group with the given <paramref name="requires" />
	///     mode: <see cref="PrerequisiteSatisfaction.Chosen" /> counts only a committed choice, while
	///     <see cref="PrerequisiteSatisfaction.Qualifying" /> also accepts a green/amber rating this run.
	/// </summary>
	internal static bool IsPrerequisiteAvailable(
		Subject required,
		PrerequisiteSatisfaction requires,
		Func<Subject, bool> qualifies,
		IReadOnlyList<Subject> chosenALevels) =>
		requires == PrerequisiteSatisfaction.Chosen
			? chosenALevels.Contains(required)
			: qualifies(required) || chosenALevels.Contains(required);

	/// <summary>
	///     The pure core of the prerequisite rule, independent of the catalogue and the rating map: one
	///     downgrade per unmet group, to that group's severity. A group is met when <paramref name="available" />
	///     holds for any of its alternatives under the group's own satisfaction mode.
	/// </summary>
	internal static IEnumerable<Adjustment> PrerequisiteAdjustments(
		Subject subject,
		Rating baseRating,
		IEnumerable<Prerequisite> groups,
		Func<Subject, PrerequisiteSatisfaction, bool> available)
	{
		foreach (var group in groups) {
			if (!group.AnyOf.Any(required => available(required, group.Requires))) {
				yield return new(subject, baseRating, group.Severity, AdjustmentKind.Prerequisite, PrerequisiteReason(group));
			}
		}
	}

	private static string PrerequisiteReason(Prerequisite group) =>
		string.Join(PrerequisiteReasonAlternativeSeparator, group.AnyOf.Select(EnumNames.NameOf))
		+ PrerequisiteReasonSuffix;

	// Mutual exclusion: when both subjects of a clash pair qualify, the lower-weight one is demoted to
	// the pair's configured severity. Amber pairs still require both to be green; red pairs block either
	// green or amber on the lower-weight side.
	private static IEnumerable<Adjustment> MutualExclusions(Dictionary<Subject, Rating> ratings, CatalogueData catalogue)
	{
		foreach (var (a, b, severity) in catalogue.ExclusionPairs) {
			var qualifies = severity == Rating.Red
				? Qualifies(ratings, a) && Qualifies(ratings, b)
				: IsGreen(ratings, a) && IsGreen(ratings, b);

			if (qualifies) {
				var (loser, winner) =
					catalogue.Meta(a).PriorityWeight <= catalogue.Meta(b).PriorityWeight ? (a, b) : (b, a);
				var winnerName = EnumNames.NameOf(winner);
				yield return new(
					loser, ratings[loser], severity, AdjustmentKind.MutualExclusion,
					severity == Rating.Red
						? $"{MutualExclusionRedReasonPrefix}{winnerName}{MutualExclusionRedReasonSuffix}"
						: $"{MutualExclusionAmberReasonPrefix}{winnerName}{MutualExclusionAmberReasonSuffix}");
			}
		}
	}

	// Prior-choice exclusions: a committed choice always wins, so every qualifying subject that excludes
	// a chosen A-level is downgraded to the configured severity.
	private static IEnumerable<Adjustment> PriorChoiceExclusions(
		Dictionary<Subject, Rating> ratings,
		IReadOnlyList<Subject> chosenALevels,
		CatalogueData catalogue)
	{
		foreach (var chosen in chosenALevels.Distinct()) {
			foreach (var exclusion in catalogue.Meta(chosen).Exclusions) {
				if (Qualifies(ratings, exclusion.Other)) {
					var chosenName = EnumNames.NameOf(chosen);
					yield return new(
						exclusion.Other, ratings[exclusion.Other], exclusion.Severity, AdjustmentKind.PriorChoiceExclusion,
						$"{PriorChoiceExclusionReasonPrefix}{chosenName}");
				}
			}
		}
	}

	// Own-time (→ amber): a green or amber subject whose required activity is absent from the student's
	// hobbies needs authorisation. Green is demoted; amber keeps its rating but records the authorisation
	// reason in the adjustment trail.
	private static IEnumerable<Adjustment> OwnTime(
		Dictionary<Subject, Rating> ratings,
		IReadOnlyList<string> hobbies,
		CatalogueData catalogue)
	{
		foreach (var subject in catalogue.Subjects.Where(subject => catalogue.Meta(subject).RequiredActivities.Count > 0)) {
			if (Qualifies(ratings, subject) && !OwnTimeSatisfied(catalogue, subject, hobbies)) {
				yield return new(subject, ratings[subject], Rating.Amber, AdjustmentKind.OwnTime, OwnTimeReason);
			}
		}
	}

	// Veto (→ red): a subject is barred outright by an incompatible activity, overriding entry/green/amber.
	// The single-student, single-subject mirror of own-time — same hobbies input, but presence triggers a
	// red veto rather than absence triggering an amber downgrade. Unlike the other rules it fires even on an
	// already-red base: Apply preserves the red severity either way, and its reason precedence keeps this
	// named bar ahead of a generic prerequisite reason when both land on the same subject.
	private static IEnumerable<Adjustment> Vetoes(
		Dictionary<Subject, Rating> ratings,
		IReadOnlyList<string> hobbies,
		CatalogueData catalogue)
	{
		foreach (var subject in catalogue.Subjects.Where(subject => catalogue.Meta(subject).BlockingActivities.Count > 0)) {
			if (VetoingHobby(catalogue, subject, hobbies) is { } hobby) {
				yield return new(subject, RequireRating(ratings, subject), Rating.Red, AdjustmentKind.Veto, $"{VetoReasonPrefix}{hobby}");
			}
		}
	}

	// Restudy bar (→ red/amber): if the student already holds a barred prior qualification in the same
	// subject, the subject is downgraded to the configured severity. Equal-severity bars replace the base
	// reason with the more informative named bar; bars less severe than the base emit no adjustment because
	// they neither determine the rating nor explain it.
	private static IEnumerable<Adjustment> RestudyBars(
		Dictionary<Subject, Rating> ratings,
		IReadOnlyList<Qualification> priorQualifications,
		CatalogueData catalogue)
	{
		foreach (var subject in catalogue.Subjects) {
			var restudyBar = catalogue.Meta(subject).RestudyBar;
			if (restudyBar is not { } bar) {
				continue;
			}

			var subjectName = EnumNames.NameOf(subject);
			var hasBarredQualification = priorQualifications.Any(qualification =>
				string.Equals(qualification.Subject, subjectName, StringComparison.OrdinalIgnoreCase)
				&& bar.Types.Contains(qualification.Type));
			if (hasBarredQualification) {
				var baseRating = RequireRating(ratings, subject);
				if (bar.Severity < baseRating) {
					continue;
				}

				yield return new(
					subject,
					baseRating,
					bar.Severity,
					AdjustmentKind.RestudyBar,
					$"{RestudyBarReasonPrefix}{subjectName}");
			}
		}
	}

	private static bool OwnTimeSatisfied(CatalogueData catalogue, Subject subject, IReadOnlyList<string> hobbies) =>
		catalogue.Meta(subject).RequiredActivities.Any(prefix =>
			hobbies.Any(hobby => hobby.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

	private static string? VetoingHobby(CatalogueData catalogue, Subject subject, IReadOnlyList<string> hobbies) =>
		hobbies.FirstOrDefault(hobby =>
			catalogue.Meta(subject).BlockingActivities.Any(prefix => hobby.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

	private static Rating RequireRating(Dictionary<Subject, Rating> ratings, Subject subject) =>
		ratings.TryGetValue(subject, out var rating)
			? rating
			: throw new InvalidDataException(
				$"Constraint pass expected a base rating for subject '{EnumNames.NameOf(subject)}'.");
}
