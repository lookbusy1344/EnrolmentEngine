namespace EnrolmentRules.Domain;

using System.Collections.Frozen;
using System.Globalization;

/// <summary>
///     The recognised GCSE subject keys (§1.1). This is the GCSE-side vocabulary, distinct from the
///     A-level <see cref="Subject" /> type: it carries <c>english_language</c> (a GCSE that gates
///     eligibility and the English subject entry rules) and omits
///     <c>further_maths</c> (an A-level with no GCSE of its own). It is the single source of truth the
///     input validator checks an incoming document's GCSE keys against, so an unknown key is rejected at
///     the boundary rather than silently treated as "not taken".
/// </summary>
public static class GcseSubjects
{
	/// <summary>The recognised GCSE subject keys (snake_case, matching the document and workflow lambdas).</summary>
	public static IReadOnlySet<string> Known { get; } = new[] {
		"maths", "english_language", "english_literature", "physics", "chemistry", "biology", "french", "german", "physical_education", "computer_studies", "history", "music", "art", "psychology", "sociology", "geography", "politics",
	}.ToFrozenSet(StringComparer.Ordinal);

	/// <summary>Whether <paramref name="subject" /> is a recognised GCSE subject key.</summary>
	public static bool IsKnown(string subject) => Known.Contains(subject);
}

/// <summary>
///     The input boundary guard (Phase 8): validates a raw <see cref="StudentInput" /> document
///     <em>before</em> it reaches prediction or the engine, so a malformed grade or an unknown subject
///     fails fast with a clear message rather than producing a silent, wrong red rating. This is the guard
///     RulesEngine does not provide for the input <em>document</em> — the workflow schema guards the
///     <em>rules</em>; this guards the facts. Pure and total: it returns the list of problems (empty ⇒
///     valid) and never throws.
/// </summary>
public static class StudentValidator
{
	/// <summary>
	///     Validate one student document. Each required object member must be present, each GCSE grade must
	///     be an integer on the [<see cref="Thresholds.MinGcseGrade" />, <see cref="Thresholds.MaxGcseGrade" />]
	///     scale, each GCSE subject key must be <see cref="GcseSubjects.Known">recognised</see>, the date of
	///     birth must be present, and every hobby tag must be non-blank. Returns one message per problem, in
	///     document order; an empty list means valid.
	/// </summary>
	public static IReadOnlyList<string> Validate(StudentInput? student, CatalogueData catalogue, QualificationScale scale)
	{
		if (student is null) {
			return ["student is required"];
		}

		return [
			.. RequiredText(student.Id, "student id"),
			.. student.Gcses is EquatableDictionary<string, int> gcses ? gcses.SelectMany(ValidateGcse) : ["gcses is required"],
			.. student.Hobbies is EquatableArray<string> hobbies
				? hobbies
				  .Index()
				  .Where(static h => string.IsNullOrWhiteSpace(h.Item))
				  .Select(static h => $"hobby tag at position {h.Index} is blank")
				: ["hobbies is required"],
			.. ValidateDateOfBirth(student.DateOfBirth),
			.. ValidateChosenALevels(student.ChosenALevels, catalogue),
			.. ValidatePriorQualifications(student.PriorQualifications, scale),
		];
	}

	/// <summary>
	///     The policy-independent subset of <see cref="Validate" />: everything except catalogue-membership
	///     of <see cref="StudentInput.ChosenALevels" />. Used by the non-destructive policy comparison
	///     projection, which classifies a chosen subject absent from the selected policy's catalogue as
	///     <c>NotOffered</c> rather than a structural validation failure — that classification would
	///     otherwise collide with this same "invalid" message. Duplicate chosen entries remain a structural
	///     error regardless of catalogue membership; see <see cref="ValidateChosenALevelsDuplicates" />.
	/// </summary>
	public static IReadOnlyList<string> ValidateFacts(StudentInput? student, QualificationScale scale)
	{
		if (student is null) {
			return ["student is required"];
		}

		return [
			.. RequiredText(student.Id, "student id"),
			.. student.Gcses is EquatableDictionary<string, int> gcses ? gcses.SelectMany(ValidateGcse) : ["gcses is required"],
			.. student.Hobbies is EquatableArray<string> hobbies
				? hobbies
				  .Index()
				  .Where(static h => string.IsNullOrWhiteSpace(h.Item))
				  .Select(static h => $"hobby tag at position {h.Index} is blank")
				: ["hobbies is required"],
			.. ValidateDateOfBirth(student.DateOfBirth),
			.. ValidateChosenALevelsDuplicates(student.ChosenALevels),
			.. ValidatePriorQualifications(student.PriorQualifications, scale),
		];
	}

	/// <summary>
	///     Duplicate <c>chosen_a_levels</c> entries only, independent of catalogue membership — the
	///     comparison path's structural check on the chosen basket (see <see cref="ValidateFacts" />).
	/// </summary>
	public static IReadOnlyList<string> ValidateChosenALevelsDuplicates(IReadOnlyList<Subject> chosenALevels)
	{
		var seen = new HashSet<Subject>();
		var errors = new List<string>();
		foreach (var (index, subject) in chosenALevels.Index()) {
			if (!seen.Add(subject)) {
				errors.Add($"chosen_a_levels entry at position {index} duplicates '{EnumNames.NameOf(subject)}'");
			}
		}

		return errors;
	}

	private static IEnumerable<string> RequiredText(string? value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value)) {
			yield return $"{fieldName} is required";
		}
	}

	private static IEnumerable<string> ValidateDateOfBirth(DateOnly? dateOfBirth)
	{
		if (dateOfBirth is null) {
			yield return "date_of_birth is required";
		}
	}

	private static IEnumerable<string> ValidateGcse(KeyValuePair<string, int> gcse)
	{
		if (string.IsNullOrWhiteSpace(gcse.Key)) {
			yield return "Empty GCSE subject";
		} else if (!GcseSubjects.IsKnown(gcse.Key)) {
			yield return $"unknown GCSE subject '{gcse.Key}'";
		}

		if (gcse.Value is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			yield return string.Create(
				CultureInfo.InvariantCulture,
				$"GCSE '{gcse.Key}' grade {gcse.Value} is out of range ({Thresholds.MinGcseGrade}–{Thresholds.MaxGcseGrade})");
		}
	}

	private static IEnumerable<string> ValidateChosenALevels(IReadOnlyList<Subject> chosenALevels, CatalogueData catalogue)
	{
		var seen = new HashSet<Subject>();
		foreach (var (index, subject) in chosenALevels.Index()) {
			if (!catalogue.Subjects.Contains(subject)) {
				yield return $"chosen_a_levels entry at position {index} is invalid: {subject.Value}";
				continue;
			}

			if (seen.Add(subject)) {
				continue;
			}

			yield return $"chosen_a_levels entry at position {index} duplicates '{EnumNames.NameOf(subject)}'";
		}
	}

	private static IEnumerable<string> ValidatePriorQualifications(
		IReadOnlyList<Qualification> priorQualifications,
		QualificationScale scale)
	{
		foreach (var (index, qualification) in priorQualifications.Index()) {
			if (string.IsNullOrWhiteSpace(qualification.Subject)) {
				yield return $"prior_qualifications entry at position {index} subject is blank";
			}

			if (!scale.TryOrdinal(qualification.Type, qualification.Grade, out _)) {
				yield return $"prior_qualifications entry at position {index} subject '{qualification.Subject}' is invalid: "
							 + $"unknown qualification {EnumNames.NameOf(qualification.Type)} grade '{qualification.Grade}'";
			}
		}
	}
}
