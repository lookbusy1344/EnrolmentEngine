namespace EnrolmentRules.Prediction;

using Domain;

/// <summary>
///     The §1.2 statistical prediction stage as pure host code, upstream of the engine: it turns raw
///     GCSE facts into the <see cref="StudentProfile" /> the rules consume. Averaging plus a catalogue-driven
///     one-feature linear regression (<see cref="PredictionModel" />) per subject, enriched with DfE
///     transition-matrix probability evidence; no engine and no state.
/// </summary>
public static class GradePredictor
{
	/// <summary>
	///     Predict from facts already projected to <see cref="GcseResult" />s using an explicit catalogue
	///     and qualification scale. The transition matrix defaults to the shipped extract; the engine
	///     threads an explicit, data-directory-sourced matrix through the overload below.
	/// </summary>
	public static StudentProfile Predict(
		StudentInput student, IReadOnlyList<GcseResult> gcses, DateOnly asOf, CatalogueData catalogue, QualificationScale scale)
		=> Predict(student, gcses, asOf, catalogue, DfeTransitionMatrix.LoadDefault(), scale);

	/// <summary>
	///     Predict using an explicit catalogue <em>and</em> transition matrix, so a host that ships its data
	///     in a non-default location drives prediction from that data rather than the shipped default.
	/// </summary>
	public static StudentProfile Predict(
		StudentInput student, IReadOnlyList<GcseResult> gcses, DateOnly asOf, CatalogueData catalogue, DfeTransitionMatrix matrix,
		QualificationScale scale)
	{
		var average = AverageGcseScore(gcses);
		var age = student.DateOfBirth.HasValue ? AgeCalculator.WholeYears(student.DateOfBirth.Value, asOf) : 0;
		var gcseByKey = gcses.ToDictionary(static g => g.Subject, static g => g.Grade, StringComparer.Ordinal);

		return new(
			student.Id,
			average,
			[
				.. catalogue.Subjects.Select(subject =>
					new PredictedGrade(
						subject,
						Math.Max(
							catalogue.Meta(subject).Regression.Predict(average),
							Math.Max(
								StrongSubjectOverridePoints(gcseByKey, subject, catalogue),
								BestEntryEquivalentPoints(student.PriorQualifications, subject, catalogue, scale))))),
			],
			[.. matrix.EvidenceFor(average, catalogue)],
			student.Hobbies ?? []) { ChosenALevels = student.ChosenALevels, PriorQualifications = student.PriorQualifications, Age = age };
	}

	/// <summary>
	///     Mean grade over the present GCSEs (absent subjects are simply not in the set). An
	///     empty set yields 0.0 rather than throwing, keeping the stage total.
	/// </summary>
	public static double AverageGcseScore(IReadOnlyCollection<GcseResult> gcses) =>
		gcses.Count == 0 ? 0.0 : gcses.Average(static g => (double)g.Grade);

	/// <summary>
	///     The strong-individual-subject override: the base prediction runs on the student's average GCSE, but
	///     a standout grade in the subject's own cognate GCSE (the GCSE whose key matches the A-level subject)
	///     should not be dragged down by a weaker average. We feed that own grade through the <em>same</em>
	///     per-subject regression line — "predict this subject as if the average equalled the student's grade in
	///     it" — and the caller takes the maximum, so the override only ever lifts a subject, never lowers it,
	///     and only bites when the individual grade beats the average. Subjects with no cognate GCSE (e.g.
	///     psychology, further_maths) have no own grade to override with and fall back to the average line.
	/// </summary>
	private static double StrongSubjectOverridePoints(
		Dictionary<string, int> gcseByKey, Subject subject, CatalogueData catalogue) =>
		gcseByKey.TryGetValue(subject.Value, out var ownGrade)
			? catalogue.Meta(subject).Regression.Predict(ownGrade)
			: ALevelGrade.Min;

	private static double BestEntryEquivalentPoints(
		IReadOnlyList<Qualification> priorQualifications,
		Subject subject,
		CatalogueData catalogue,
		QualificationScale scale)
	{
		var best = ALevelGrade.Min;
		foreach (var equivalent in catalogue.Meta(subject).EntryEquivalents) {
			foreach (var qualification in priorQualifications) {
				if (scale.Satisfies(qualification, equivalent)) {
					best = Math.Max(best, scale.Equivalence(qualification.Type, qualification.Grade));
				}
			}
		}

		return best;
	}
}
