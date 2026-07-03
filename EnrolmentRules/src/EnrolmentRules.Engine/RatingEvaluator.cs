#pragma warning disable CS3019
namespace EnrolmentRules.Engine;

using Domain;
using RulesEngine.Interfaces;
using RulesEngine.Models;

/// <summary>
///     The eligibility verdict (§1.3) assembled in host code from the per-requirement
///     <see cref="RuleResultTree" />s the engine returns. The engine cannot aggregate sibling rules
///     (Reservation 2), but it <em>can</em> report each requirement's pass/fail, which is all the gate
///     needs. <see cref="Reasons" /> lists the failed requirements in the workflow's declared precedence
///     (English → Maths → pass-count); an empty list means eligible.
/// </summary>
internal sealed record EligibilityGate(bool Eligible, EquatableArray<string> Reasons);

/// <summary>
///     A subject's base traffic-light rating as decided by the engine alone (§1.4), before any host-code
///     cross-subject adjustments. <see cref="Reason" /> is the deciding rule's <c>SuccessEvent</c>.
/// </summary>
internal sealed record SubjectRating(Subject Subject, Rating Rating, string Reason);

/// <summary>
///     Runs the rules-as-data workflows over one student's facts and composes the host-side verdicts.
///     Stateless and thread-safe: it holds only the shared, reusable engine.
/// </summary>
[CLSCompliant(false)]
internal sealed class RatingEvaluator(
	IRulesEngine engine,
	PolicyThresholds thresholds,
	CatalogueData? catalogue = null,
	QualificationScale? scale = null)
{
	public const string EligibilityWorkflow = "eligibility";
	public const string SubjectRatingsWorkflow = "subject-ratings";
	public const string EnglishLanguagePassRule = "EnglishLanguagePass";
	public const string MathsPassRule = "MathsPass";
	public const string EnoughPassesRule = "EnoughPasses";

	/// <summary>Separator between the subject and rating segments of a subject-rating rule name.</summary>
	public const char RuleNameSeparator = ':';

	private readonly PolicyFacts policy = new(thresholds);

	/// <summary>The loaded policy thresholds this evaluator binds into the workflows and exposes to the host pass.</summary>
	public PolicyThresholds Thresholds { get; } = thresholds;

	/// <summary>The catalogue this evaluator binds into prediction, entry-equivalent checks, and host validation.</summary>
	public CatalogueData Catalogue { get; } = catalogue ?? Domain.Catalogue.Default;

	/// <summary>The qualification scale this evaluator binds into prediction and entry-equivalent checks.</summary>
	public QualificationScale Scale { get; } = scale ?? QualificationScale.Default;

	/// <summary>
	///     Shape one student's GCSEs into the two engine inputs the eligibility workflow binds to: the
	///     <c>gcses</c> <em>array</em> the pass-count iterates, and the <c>lookup</c> keyed accessor the
	///     single-subject requirements read (absent subject ⇒ grade 0 ⇒ not a pass). Keeping the count on
	///     the array and the lookups on the accessor is pinned by a test so the two never get crossed.
	/// </summary>
	public static RuleParameter[] EligibilityParameters(IReadOnlyList<GcseResult> gcses, PolicyThresholds thresholds) => [
		new("gcses", gcses.ToArray()),
		new("lookup", new GcseFacts(gcses)),
		new("policy", new PolicyFacts(thresholds)),
	];

	internal static RuleParameter[] EligibilityParameters(IReadOnlyList<GcseResult> gcses, GcseFacts lookup, PolicyFacts policy) => [
		new("gcses", gcses.ToArray()),
		new("lookup", lookup),
		new("policy", policy),
	];

	/// <summary>
	///     Evaluate the §1.3 gate. Composes <see cref="EligibilityGate" /> from the workflow results,
	///     preserving the rules' declared order so the reasons keep the English → Maths → pass-count
	///     precedence.
	/// </summary>
	public EligibilityGate EvaluateEligibility(IReadOnlyList<GcseResult> gcses, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var lookup = new GcseFacts(gcses);
		var results = ExecuteAllRules(EligibilityWorkflow, EligibilityParameters(gcses, lookup, policy));
		return EvaluateEligibility(results);
	}

	private EligibilityGate EvaluateEligibility(IReadOnlyList<RuleResultTree> results)
	{
		var reasons = results
			.Where(static r => !r.IsSuccess)
			.Select(result => EligibilityFailureReason(result))
			.ToList();

		return new(reasons.Count == 0, EquatableArray.CopyOf(reasons));
	}

	/// <summary>
	///     The full base verdict for one student <em>with its eligibility gate</em>: the §1.3 gate
	///     short-circuited over the §1.4 per-subject ratings (Phase 4). The gate is returned alongside the
	///     ratings so the downstream façade can report eligibility and choose the right provenance without
	///     re-running the eligibility workflow. An ineligible student is red in every <see cref="Subject" />
	///     carrying the gate reason and the per-subject workflow is never run.
	/// </summary>
	public (EligibilityGate Gate, IReadOnlyList<SubjectRating> Ratings) EvaluateWithGate(
		StudentProfile profile,
		IReadOnlyList<GcseResult> gcses,
		CancellationToken cancellationToken = default)
	{
		var lookup = new GcseFacts(gcses);
		return EvaluateWithGate(profile, gcses, lookup, cancellationToken);
	}

	internal (EligibilityGate Gate, IReadOnlyList<SubjectRating> Ratings) EvaluateWithGate(
		StudentProfile profile,
		IReadOnlyList<GcseResult> gcses,
		GcseFacts lookup,
		CancellationToken cancellationToken = default)
	{
		var gate = EvaluateEligibility(gcses, lookup, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		var ratings = gate.Eligible
			? EvaluateRatings(profile, lookup, cancellationToken)
			: AllRed(gate.Reasons[0]);
		return (gate, ratings);
	}

	internal EligibilityGate EvaluateEligibility(
		IReadOnlyList<GcseResult> gcses,
		GcseFacts lookup,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var results = ExecuteAllRules(EligibilityWorkflow, EligibilityParameters(gcses, lookup, policy));
		return EvaluateEligibility(results);
	}

	/// <summary>
	///     The base per-subject ratings (Phase 4), discarding the gate. Convenience over
	///     <see cref="EvaluateWithGate(StudentProfile, IReadOnlyList{GcseResult}, CancellationToken)" /> for
	///     callers that only need the ratings.
	/// </summary>
	public IReadOnlyList<SubjectRating> Evaluate(
		StudentProfile profile,
		IReadOnlyList<GcseResult> gcses,
		CancellationToken cancellationToken = default) =>
		EvaluateWithGate(profile, gcses, cancellationToken).Ratings;

	/// <summary>
	///     The ineligible short-circuit output: one red <see cref="SubjectRating" /> per <see cref="Subject" />
	///     carrying the gate reason. The subject set is data-driven from the <see cref="Subject" /> type, so adding a subject can't
	///     silently skip the gate.
	/// </summary>
	private IReadOnlyList<SubjectRating> AllRed(string gateReason) =>
		[.. Catalogue.Subjects.Select(subject => new SubjectRating(subject, Rating.Red, gateReason))];

	/// <summary>
	///     Evaluate the §1.4 per-subject entry + rating tiers and return one base <see cref="SubjectRating" />
	///     per <see cref="Subject" />. "First hit wins" is realised here, not by the engine: the engine runs
	///     every rule, so for each subject we take the <em>first successful rule in declared order</em> (the
	///     tiers are authored green → amber → catch-all red, and a green-eligible student also satisfies the
	///     amber rule — taking the first is what makes green win).
	/// </summary>
	public IReadOnlyList<SubjectRating> EvaluateRatings(
		StudentProfile profile,
		IReadOnlyList<GcseResult> gcses,
		CancellationToken cancellationToken = default)
	{
		var lookup = new GcseFacts(gcses);
		return EvaluateRatings(profile, lookup, cancellationToken);
	}

	internal IReadOnlyList<SubjectRating> EvaluateRatings(
		StudentProfile profile,
		GcseFacts gcses,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var results = ExecuteAllRules(
			SubjectRatingsWorkflow, new RuleParameter("facts", new RatingFacts(profile, gcses, policy, Catalogue, Scale)));

		var winners = new Dictionary<Subject, SubjectRating>();
		foreach (var result in results.Where(static r => r.IsSuccess)) {
			var (subject, rating) = ParseRuleName(result.Rule.RuleName);
			if (!winners.ContainsKey(subject)) {
				winners[subject] = new(subject, rating, result.Rule.SuccessEvent ?? string.Empty);
			}
		}

		return [
			.. Catalogue.Subjects.Select(subject =>
				winners.TryGetValue(subject, out var rating)
					? rating
					: throw new WorkflowProbeException(
						SubjectRatingsWorkflow, $"no rule produced a rating for subject '{EnumNames.NameOf(subject)}'")),
		];
	}

	// RulesEngine evaluates the compiled lambdas synchronously here (no I/O and no configured action
	// workflows). If the task is still incomplete, fail fast rather than silently reintroducing a hidden
	// blocking wait on the hot path. If it already completed, GetResult preserves the original fault.
	private List<RuleResultTree> ExecuteAllRules(string workflow, params RuleParameter[] facts)
	{
		var execution = engine.ExecuteAllRulesAsync(workflow, facts);
		if (execution.IsCompleted) {
#pragma warning disable VSTHRD002 // The completion guard above guarantees this ValueTask has already finished.
			return execution.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
		}

		throw new InvalidOperationException(
			$"RulesEngine workflow '{workflow}' did not complete synchronously on the synchronous evaluation path.");
	}

	/// <summary>
	///     The deciding rule name for a (subject, rating) pair — the inverse of <see cref="ParseRuleName" />.
	///     Used by the explanation projection to name the engine rule that produced a base rating.
	/// </summary>
	public static string RuleName(Subject subject, Rating rating) =>
		$"{EnumNames.NameOf(subject)}{RuleNameSeparator}{EnumNames.NameOf(rating)}";

	/// <summary>Split a <c>"{subject}:{rating}"</c> rule name into its typed parts; throws on drift/typo.</summary>
	private static (Subject Subject, Rating Rating) ParseRuleName(string ruleName)
	{
		var parts = ruleName.Split(RuleNameSeparator);
		if (parts is [var subjectName, var ratingName]
			&& Subject.TryParse(subjectName, out var subject)
			&& EnumNames.TryParse<Rating>(ratingName, out var rating)) {
			return (subject, rating);
		}

		throw new WorkflowProbeException(
			SubjectRatingsWorkflow, $"rule name '{ruleName}' is not a valid '<subject>{RuleNameSeparator}<rating>' pair");
	}

	private string EligibilityFailureReason(RuleResultTree result) =>
		result.Rule.RuleName switch {
			EnglishLanguagePassRule => $"GCSE English Language below the pass grade ({Thresholds.PassGrade})",
			MathsPassRule => $"GCSE Maths below the pass grade ({Thresholds.PassGrade})",
			EnoughPassesRule => $"Fewer than the required number of GCSE passes ({Thresholds.MinPasses} at grade {Thresholds.PassGrade} or above)",
			_ => throw new WorkflowProbeException(
				EligibilityWorkflow, $"unknown eligibility rule '{result.Rule.RuleName}' cannot be projected into a threshold-aware failure reason"),
		};
}

/// <summary>
///     An absent-safe keyed accessor over a student's GCSEs for the single-subject lookups in the
///     workflow lambdas (<c>lookup.Grade("maths")</c>). An absent subject returns
///     <see cref="Domain.Thresholds.MinGcseGrade" /> minus one, i.e. a non-pass sentinel of 0, so a
///     missing GCSE can never satisfy a threshold. Registered in <see cref="RuleSettings" /> custom types
///     so the engine permits the instance-method call.
/// </summary>
internal sealed class GcseFacts(IEnumerable<GcseResult> gcses)
{
	// One below the GCSE floor: a non-pass sentinel that can never satisfy a threshold, derived from the
	// scale so it tracks Thresholds.MinGcseGrade rather than drifting from a hard-coded literal.
	private const int NotTaken = Thresholds.MinGcseGrade - 1;

	// Defensive against a repeated subject key: keep the best grade (real inputs come from a
	// dictionary and carry no duplicates, but the engine inputs are a plain list).
	private readonly IReadOnlyDictionary<string, int> byKey = gcses
		.GroupBy(static g => g.Subject, StringComparer.OrdinalIgnoreCase)
		.ToDictionary(static grp => grp.Key, static grp => grp.Max(static g => g.Grade), StringComparer.OrdinalIgnoreCase);

	public int Grade(string subject) => byKey.GetValueOrDefault(subject, NotTaken);
}

/// <summary>
///     The per-subject rating workflow's single input: an absent-safe accessor over one student's facts.
///     Lambdas read <c>facts.Gcse("maths")</c> (supporting-GCSE entry checks), <c>facts.Predicted("maths")</c>
///     (the predicted A-level points the tiers compare), <c>facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.A)</c>
///     (DfE transition-matrix probability evidence), <c>facts.Average</c> (the §1.4 average-based entry
///     rules) and <c>facts.Age</c> (the §1.1 age-gated entry rules). A subject with no prediction or transition row returns zero evidence so it can
///     never clear
///     a probability-gated tier. Registered in <see cref="RuleSettings" /> custom types for the method calls.
/// </summary>
internal sealed class RatingFacts(
	StudentProfile profile,
	GcseFacts gcses,
	PolicyFacts policy,
	CatalogueData catalogue,
	QualificationScale scale)
{
	private readonly HashSet<string> entryEquivalentSubjects = BuildEntryEquivalentSubjects(profile, catalogue, scale);
	private readonly PolicyFacts policy = policy;

	private readonly IReadOnlyDictionary<string, double> predicted = profile.PredictedGrades.ToDictionary(
		static p => EnumNames.NameOf(p.Subject), static p => p.PredictedPoints);

	private readonly Dictionary<string, TransitionEvidence> transitionEvidence = profile.TransitionEvidence.ToDictionary(
		static e => EnumNames.NameOf(e.Subject), static e => e);

	/// <summary>The student's mean GCSE score (the average-based entry feature).</summary>
	public double Average { get; } = profile.AverageGcseScore;

	/// <summary>The student's age in years (the age-gated entry feature).</summary>
	public int Age { get; } = profile.Age;

	// The policy knobs the rating lambdas read, forwarded from the single PolicyFacts surface rather than
	// copied — one source of truth for the values. Eligibility-only and host-aggregation knobs (PassGrade,
	// MinPasses, MaxGreenChoices, AmberTariffFactor) are deliberately absent: no rating rule references them.
	public int TopEntry => policy.TopEntry;

	public int StrongEntry => policy.StrongEntry;

	public int StandardEntry => policy.StandardEntry;

	public double FurtherMathsAverageEntry => policy.FurtherMathsAverageEntry;

	public double HumanitiesAverageEntry => policy.HumanitiesAverageEntry;

	public double MinDfeGreenProbabilityAtOrAbove => policy.MinDfeGreenProbabilityAtOrAbove;

	public double MinDfeAmberProbabilityAtOrAbove => policy.MinDfeAmberProbabilityAtOrAbove;

	public int AdultAge => policy.AdultAge;

	/// <summary>The student's GCSE grade in <paramref name="subject" /> (0 if not taken).</summary>
	public int Gcse(string subject) => gcses.Grade(subject);

	/// <summary>The predicted A-level points for <paramref name="subject" /> (U if unmodelled).</summary>
	public double Predicted(string subject) => predicted.GetValueOrDefault(subject, ALevelGrade.Min);

	/// <summary>The DfE transition-matrix probability for <paramref name="subject" /> at or above a grade threshold.</summary>
	public double DfeProbabilityAtOrAbove(string subject, double minimumGrade) =>
		transitionEvidence.TryGetValue(subject, out var evidence) ? evidence.ProbabilityAtOrAbove(minimumGrade) : 0.0;

	/// <summary>Whether the student holds any prior qualification that satisfies the named subject's entry policy.</summary>
	public bool HasEntryEquivalent(string subject) => entryEquivalentSubjects.Contains(subject);

	private static HashSet<string> BuildEntryEquivalentSubjects(StudentProfile profile, CatalogueData catalogue, QualificationScale scale)
	{
		var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var subject in catalogue.Subjects) {
			if (catalogue.Meta(subject).EntryEquivalents.Any(entryEquivalent =>
					profile.PriorQualifications.Any(qualification => scale.Satisfies(qualification, entryEquivalent)))) {
				_ = subjects.Add(EnumNames.NameOf(subject));
			}
		}

		return subjects;
	}
}
