namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     The top-level student document as it appears on disk / on the wire:
///     <c>{ "student": { "id": ..., "gcses": {...}, "hobbies": [...] } }</c> (§1.1).
/// </summary>
public sealed record StudentDocument(StudentInput Student);

/// <summary>
///     Raw student facts (§1.1): GCSE results keyed by subject (1–9 grades; an absent key means
///     "not taken") plus opaque free-form activity tags. This is the deserialisation target and the
///     canonical immutable input the prediction stage consumes — it carries no derived values.
/// </summary>
[method: JsonConstructor]
public sealed record StudentInput(
	string Id,
	EquatableDictionary<string, int>? Gcses,
	EquatableArray<string>? Hobbies)
{
	public StudentInput(string id, IReadOnlyDictionary<string, int>? gcses, IReadOnlyList<string>? hobbies)
		: this(
			id,
			gcses is null ? null : EquatableDictionaryFactory.CopyOf(gcses),
			hobbies is null ? null : EquatableArray.CopyOf(hobbies))
	{
	}

	/// <summary>The A-level subjects the student has already chosen. These are constraint triggers, not filters.</summary>
	public EquatableArray<Subject> ChosenALevels { get; init; } = [];

	/// <summary>The student's previously held qualifications, carried as typed facts for entry and bars.</summary>
	public EquatableArray<Qualification> PriorQualifications { get; init; } = [];

	/// <summary>
	///     The student's date of birth (§1.1) — the raw fact age-gated entry rules derive from. Age is not
	///     stored on the document (it is not a pure function of the document; it depends on a reference date),
	///     it is computed by the prediction stage as of the run's reference date via
	///     <see cref="AgeCalculator.WholeYears" />. A required document member: like <see cref="Gcses" /> it is
	///     nullable here only so its absence can be reported by <see cref="StudentValidator" />.
	/// </summary>
	public DateOnly? DateOfBirth { get; init; }

	/// <summary>
	///     Project the <c>subject -&gt; grade</c> map to the array shape the engine and the
	///     averaging stage bind to. Order follows the document's enumeration order.
	/// </summary>
	public IReadOnlyList<GcseResult> ToGcseResults() =>
		[.. (Gcses ?? default).Select(static kv => new GcseResult(kv.Key, kv.Value))];
}

/// <summary>
///     A single GCSE result: the subject key (e.g. <c>english_language</c>, a GCSE subject,
///     not necessarily an A-level <see cref="Subject" />) and its 1–9 grade.
/// </summary>
public readonly record struct GcseResult(string Subject, int Grade);

/// <summary>
///     The prediction output (§1.2): the derived facts the rules consume. <see cref="AverageGcseScore" />
///     is the mean over present GCSEs; <see cref="PredictedGrades" /> carries one continuous A-level
///     points prediction per <see cref="Subject" />. Hobbies are carried through unchanged for the
///     downstream own-time constraint.
/// </summary>
[method: JsonConstructor]
public sealed record StudentProfile(
	string Id,
	double AverageGcseScore,
	EquatableArray<PredictedGrade> PredictedGrades,
	EquatableArray<TransitionEvidence> TransitionEvidence,
	EquatableArray<string> Hobbies)
{
	public StudentProfile(
		string id,
		double averageGcseScore,
		IReadOnlyList<PredictedGrade> predictedGrades,
		IReadOnlyList<TransitionEvidence> transitionEvidence,
		IReadOnlyList<string> hobbies)
		: this(
			id,
			averageGcseScore,
			EquatableArray.CopyOf(predictedGrades),
			EquatableArray.CopyOf(transitionEvidence),
			EquatableArray.CopyOf(hobbies))
	{
	}

	/// <summary>The A-level subjects already chosen by the student, carried through for host-code constraints.</summary>
	public EquatableArray<Subject> ChosenALevels { get; init; } = [];

	/// <summary>The student's prior qualifications, carried through unchanged for downstream policy checks.</summary>
	public EquatableArray<Qualification> PriorQualifications { get; init; } = [];

	/// <summary>
	///     The student's age in whole years, derived by the prediction stage from the input
	///     <see cref="StudentInput.DateOfBirth" /> as of the run's reference date, for age-gated entry rules.
	/// </summary>
	public int Age { get; init; }
}

/// <summary>
///     A predicted A-level result: the subject and its continuous predicted points on the
///     <see cref="ALevelGrade" /> scale (clamped to [<see cref="ALevelGrade.Min" />,
///     <see cref="ALevelGrade.Max" />]).
/// </summary>
public readonly record struct PredictedGrade(Subject Subject, double PredictedPoints);

/// <summary>
///     DfE national transition-matrix evidence for a subject at the student's prior-attainment band.
///     Probabilities are proportions in [0, 1] for the A-level grade columns in the source workbook.
/// </summary>
public sealed record TransitionEvidence(
	Subject Subject,
	string Source,
	string PriorAttainmentBand,
	double ProbabilityU,
	double ProbabilityE,
	double ProbabilityD,
	double ProbabilityC,
	double ProbabilityB,
	double ProbabilityA,
	double ProbabilityAStar)
{
	/// <summary>
	///     The student's own prior-attainment band when it differs from <see cref="PriorAttainmentBand" />
	///     because the matrix had no row for it and the nearest populated band supplied the probabilities;
	///     <c>null</c> for an exact match (and for an unmodelled subject's empty evidence). Makes the
	///     otherwise-silent nearest-band substitution observable — <see cref="PriorAttainmentBand" /> names
	///     the band the probabilities describe, this names the band actually asked for.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? RequestedBand { get; init; }

	/// <summary>Whether the probabilities were imputed from a neighbouring band rather than the student's own.</summary>
	public bool Imputed => RequestedBand is not null;

	/// <summary>Probability of achieving at least <paramref name="minimumGrade" /> on the A-level points scale.</summary>
	public double ProbabilityAtOrAbove(double minimumGrade) => minimumGrade switch {
		<= ALevelGrade.U => ProbabilityU + ProbabilityE + ProbabilityD + ProbabilityC + ProbabilityB + ProbabilityA + ProbabilityAStar,
		<= ALevelGrade.E => ProbabilityE + ProbabilityD + ProbabilityC + ProbabilityB + ProbabilityA + ProbabilityAStar,
		<= ALevelGrade.D => ProbabilityD + ProbabilityC + ProbabilityB + ProbabilityA + ProbabilityAStar,
		<= ALevelGrade.C => ProbabilityC + ProbabilityB + ProbabilityA + ProbabilityAStar,
		<= ALevelGrade.B => ProbabilityB + ProbabilityA + ProbabilityAStar,
		<= ALevelGrade.A => ProbabilityA + ProbabilityAStar,
		<= ALevelGrade.AStar => ProbabilityAStar,
		_ => 0.0,
	};
}
