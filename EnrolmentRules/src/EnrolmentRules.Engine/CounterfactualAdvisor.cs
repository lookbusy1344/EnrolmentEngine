namespace EnrolmentRules.Engine;

using System.Collections.Concurrent;
using Domain;

/// <summary>
///     Counterfactual grade search over the same engine-backed pipeline. The search stays host-side and
///     evaluates perturbed students through the existing façade, so it inherits the workflow rules
///     without reimplementing them.
/// </summary>
internal static class CounterfactualAdvisor
{
	private const string BudgetExhaustedReason = "budget exhausted";

	public static async Task<AdviceResult> AdviseAsync(
		EnrolmentEngine engine,
		StudentInput student,
		PolicyThresholds thresholds,
		DateOnly asOf,
		bool considerUnsatGcses,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var explained = await engine.ExplainAsync(student, asOf, cancellationToken).ConfigureAwait(false);
		if (!explained.Eligible) {
			return new(
				false,
				[.. explained.EligibilityReasons],
				[],
				new(BuildGateClearingAdvice(student, thresholds)));
		}

		// One evaluation cache shared across every subject's search: the searches all perturb the same
		// student and explore heavily overlapping grade vectors, and a full EnrolmentResult is a pure
		// function of the (fixed-hobbies) grade vector, so a vector evaluated for one subject is reused by
		// the rest. Concurrent because the per-subject searches run in parallel below.
		var evaluations = new ConcurrentDictionary<string, EnrolmentResult>(StringComparer.Ordinal);

		// The per-subject reachability search only ever raises GCSEs the student already sat: a grade bump is
		// actionable advice, "sit another GCSE from scratch" is not. Restricting the candidate set to the held
		// subjects is also what keeps the search tractable — its state space is exponential in the number of
		// candidates. A subject gated on a GCSE the student never took is then unreachable by grade changes
		// alone, which ClassifyBlockedReasonAsync surfaces as that entry rule's own reason. The
		// considerUnsatGcses diagnostic knob reverts to the old, heavier search over every known GCSE.
		var candidates = considerUnsatGcses ? AdvisorCandidates.AllSubjects : HeldSubjects(student);

		return new(
			true,
			[.. explained.EligibilityReasons],
			[
				.. await Task.WhenAll(
						explained.Explanations
							.Where(static explanation => explanation.Rating is Rating.Red or Rating.Amber)
							.Select(explanation =>
								BuildSubjectAdviceAsync(engine, student, explanation, candidates, evaluations, asOf, cancellationToken)))
					.ConfigureAwait(false),
			],
			null);
	}

	private static async Task<SubjectAdvice> BuildSubjectAdviceAsync(
		EnrolmentEngine engine,
		StudentInput student,
		Explanation explanation,
		IReadOnlyList<string> candidates,
		ConcurrentDictionary<string, EnrolmentResult> evaluations,
		DateOnly asOf,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var target = explanation.Rating == Rating.Red ? Rating.Amber : Rating.Green;
		var search = await SearchAsync(
			engine,
			student,
			candidates,
			evaluations,
			finalResult => finalResult.Recommendations.Single(r => r.Subject == explanation.Subject).Rating == target,
			asOf,
			cancellationToken).ConfigureAwait(false);

		var blockedReason = search.Reachable
			? null
			: await ClassifyBlockedReasonAsync(engine, student, explanation, target, candidates, evaluations, asOf, cancellationToken)
				.ConfigureAwait(false);

		return new(
			explanation.Subject,
			explanation.Rating,
			target,
			search.Changes,
			search.Reachable,
			blockedReason);
	}

	// When the search fails, decide why by probing the student with every candidate (held) GCSE maxed: the
	// entry requirements those grades can reach are then certainly met, so if the subject still falls short of
	// the target it is held there by something grades can never undo — a non-grade adjustment (prerequisite,
	// cap, veto, mutual or prior-choice exclusion) or an entry rule gated on a GCSE the student never sat —
	// so surface that rule's own reason. Only when even maxed held grades reach the target was the block
	// merely the grade budget. This catches a prerequisite that activates only once grades lift the subject
	// into qualifying (e.g. Further Maths' chosen-Maths rule), which the original rating cannot reveal because
	// the subject was still red on entry.
	private static async Task<string> ClassifyBlockedReasonAsync(
		EnrolmentEngine engine,
		StudentInput student,
		Explanation explanation,
		Rating target,
		IReadOnlyList<string> candidates,
		ConcurrentDictionary<string, EnrolmentResult> evaluations,
		DateOnly asOf,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var restudyReason = explanation.Overrides
			.FirstOrDefault(static override_ =>
				override_.Reason.StartsWith(ConstraintPass.RestudyBarReasonPrefix, StringComparison.Ordinal))
			?.Reason;
		if (restudyReason is not null) {
			return restudyReason;
		}

		var maxed = candidates.ToDictionary(
			static gcse => gcse, static _ => Thresholds.MaxGcseGrade, StringComparer.OrdinalIgnoreCase);
		var result = await EvaluateCachedAsync(engine, student, maxed, candidates, evaluations, asOf, cancellationToken).ConfigureAwait(false);
		var recommendation = result.Recommendations.Single(r => r.Subject == explanation.Subject);

		// Ratings ascend in severity (green < amber < red), so "at least as good as target" is <=.
		return (int)recommendation.Rating <= (int)target
			? BudgetExhaustedReason
			: recommendation.Reason;
	}

	private static async Task<SearchResult> SearchAsync(
		EnrolmentEngine engine,
		StudentInput student,
		IReadOnlyList<string> candidates,
		ConcurrentDictionary<string, EnrolmentResult> evaluations,
		Func<EnrolmentResult, bool> predicate,
		DateOnly asOf,
		CancellationToken cancellationToken)
	{
		var original = GradeMap(student);
		var queue = new PriorityQueue<SearchState, (int Cost, int TouchedSubjects, int Sequence)>();
		var visited = new HashSet<string>(StringComparer.Ordinal);
		var sequence = 0;

		var start = new SearchState(original, original, [], 0);
		queue.Enqueue(start, (0, 0, sequence++));
		_ = visited.Add(Fingerprint(original, candidates));

		while (queue.Count > 0) {
			cancellationToken.ThrowIfCancellationRequested();
			var state = queue.Dequeue();
			var result = await EvaluateCachedAsync(engine, student, state.Grades, candidates, evaluations, asOf, cancellationToken)
				.ConfigureAwait(false);
			if (predicate(result)) {
				return new(true, [.. state.Changes]);
			}

			if (state.Cost >= AdvisorBudget.MaxCost) {
				continue;
			}

			foreach (var subject in candidates) {
				var current = state.Grades.GetValueOrDefault(subject, 0);
				if (current >= Thresholds.MaxGcseGrade) {
					continue;
				}

				var nextValue = current + 1;
				var originalGrade = original.GetValueOrDefault(subject, 0);

				var nextGrades = new Dictionary<string, int>(state.Grades, StringComparer.OrdinalIgnoreCase) { [subject] = nextValue };
				var nextChanged = new HashSet<string>(state.ChangedSubjects, StringComparer.OrdinalIgnoreCase);
				if (originalGrade != nextValue) {
					_ = nextChanged.Add(subject);
				}

				if (nextChanged.Count > AdvisorBudget.MaxSubjects) {
					continue;
				}

				var fingerprint = Fingerprint(nextGrades, candidates);
				if (!visited.Add(fingerprint)) {
					continue;
				}

				var nextState = new SearchState(nextGrades, state.OriginalGrades, nextChanged, state.Cost + 1);
				queue.Enqueue(nextState, (nextState.Cost, nextChanged.Count, sequence++));
			}
		}

		return new(false, []);
	}

	private static EquatableArray<GradeChange> BuildGateClearingAdvice(StudentInput student, PolicyThresholds thresholds)
	{
		var grades = GradeMap(student);
		var changes = new List<GradeChange>();
		var passes = grades.Values.Count(grade => grade >= thresholds.PassGrade);

		void RaiseToPass(string subject)
		{
			var current = grades.GetValueOrDefault(subject, 0);
			if (current >= thresholds.PassGrade) {
				return;
			}

			grades[subject] = thresholds.PassGrade;
			changes.Add(new(subject, current, thresholds.PassGrade));
			passes++;
		}

		RaiseToPass("english_language");
		RaiseToPass("maths");

		if (passes >= thresholds.MinPasses) {
			return [.. changes];
		}

		foreach (var subject in AdvisorCandidates.AllSubjects
					 .Where(static subject => subject is not "english_language" and not "maths")
					 .Select(subject => new
					 {
						 Subject = subject,
						 Current = grades.GetValueOrDefault(subject, 0),
						 Cost = Math.Max(0, thresholds.PassGrade - grades.GetValueOrDefault(subject, 0)),
					 })
					 .Where(candidate => candidate.Current < thresholds.PassGrade)
					 .OrderBy(static candidate => candidate.Cost)
					 .ThenBy(static candidate => candidate.Subject, StringComparer.Ordinal)) {
			grades[subject.Subject] = thresholds.PassGrade;
			changes.Add(new(subject.Subject, subject.Current, thresholds.PassGrade));
			passes++;

			if (passes >= thresholds.MinPasses) {
				break;
			}
		}

		return [.. changes];
	}

	// Evaluate the student with these grades, reusing a prior result for the same grade vector. The race
	// between two parallel searches missing the same key is benign: both compute the same result and the
	// second write replaces an identical value.
	private static async Task<EnrolmentResult> EvaluateCachedAsync(
		EnrolmentEngine engine,
		StudentInput student,
		Dictionary<string, int> grades,
		IReadOnlyList<string> candidates,
		ConcurrentDictionary<string, EnrolmentResult> evaluations,
		DateOnly asOf,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var fingerprint = Fingerprint(grades, candidates);
		if (evaluations.TryGetValue(fingerprint, out var cached)) {
			return cached;
		}

		var result = await engine.EvaluateAsync(Apply(student, grades), asOf, cancellationToken).ConfigureAwait(false);
		evaluations[fingerprint] = result;
		return result;
	}

	private static StudentInput Apply(StudentInput student, IReadOnlyDictionary<string, int> grades) =>
		new(student.Id, GradeMap(grades), student.Hobbies) { ChosenALevels = student.ChosenALevels };

	private static Dictionary<string, int> GradeMap(StudentInput student) => GradeMap(student.Gcses);

	// A fresh case-insensitive grade map, the shape the search perturbs and the engine input binds to.
	// Shared by the student-seed and the per-state copy: both need an independent, comparer-stable dictionary.
	private static Dictionary<string, int> GradeMap(IReadOnlyDictionary<string, int>? grades) =>
		grades is null ? new(StringComparer.OrdinalIgnoreCase) : new(grades, StringComparer.OrdinalIgnoreCase);

	// The search only ever perturbs the candidate subjects, so a fingerprint over those keys fully identifies
	// a state. Keying on the per-call candidate set (not the full GCSE list) keeps the key compact.
	private static string Fingerprint(IReadOnlyDictionary<string, int> grades, IReadOnlyList<string> candidates) =>
		string.Join('|', candidates.Select(subject => $"{subject}:{grades.GetValueOrDefault(subject, 0)}"));

	// The GCSEs the student actually sat, in a stable order — the candidate set the per-subject search raises.
	private static IReadOnlyList<string> HeldSubjects(StudentInput student) =>
		[.. GradeMap(student).Keys.OrderBy(static subject => subject, StringComparer.Ordinal)];

	private sealed class SearchState(
		Dictionary<string, int> grades,
		Dictionary<string, int> originalGrades,
		HashSet<string> changedSubjects,
		int cost)
	{
		public Dictionary<string, int> Grades { get; } = grades;
		public Dictionary<string, int> OriginalGrades { get; } = originalGrades;
		public HashSet<string> ChangedSubjects { get; } = changedSubjects;
		public int Cost { get; } = cost;

		public EquatableArray<GradeChange> Changes => [
			.. ChangedSubjects
				.OrderBy(static subject => subject, StringComparer.Ordinal)
				.Select(subject => new GradeChange(
					subject,
					OriginalGrades.GetValueOrDefault(subject, 0),
					Grades.GetValueOrDefault(subject, OriginalGrades.GetValueOrDefault(subject, 0)))),
		];
	}

	private readonly record struct SearchResult(bool Reachable, EquatableArray<GradeChange> Changes);

	private static class AdvisorCandidates
	{
		public static IReadOnlyList<string> AllSubjects { get; } =
			[.. GcseSubjects.Known.OrderBy(static subject => subject, StringComparer.Ordinal)];
	}
}

/// <summary>Named advisor budgets, kept separate from the search so the limits are not magic numbers.</summary>
public static class AdvisorBudget
{
	public const int MaxCost = 12;
	public const int MaxSubjects = 3;
}
