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
	///     Predict a profile from raw student facts as of <paramref name="asOf" /> (the run's reference date,
	///     which fixes the student's derived age): mean GCSE score and one clamped A-level prediction per
	///     <see cref="Subject" />.
	/// </summary>
	public static StudentProfile Predict(StudentInput student, DateOnly asOf) =>
		Predict(student, student.ToGcseResults(), asOf, Catalogue.Current);

	/// <summary>
	///     Predict from facts already projected to <see cref="GcseResult" />s. The pipeline entry points
	///     materialise the GCSE list once for the engine and reuse it here, so the projection isn't repeated.
	///     <paramref name="asOf" /> is the reference date the student's age is computed against.
	/// </summary>
	public static StudentProfile Predict(StudentInput student, IReadOnlyList<GcseResult> gcses, DateOnly asOf)
		=> Predict(student, gcses, asOf, Catalogue.Current);

	/// <summary>
	///     Predict from facts already projected to <see cref="GcseResult" />s using an explicit catalogue.
	///     The pipeline entry points materialise the GCSE list once for the engine and reuse it here, so the
	///     projection isn't repeated. <paramref name="asOf" /> is the reference date the student's age is
	///     computed against. The transition matrix defaults to the shipped extract; the engine threads an
	///     explicit, data-directory-sourced matrix through the overload below.
	/// </summary>
	public static StudentProfile Predict(StudentInput student, IReadOnlyList<GcseResult> gcses, DateOnly asOf, CatalogueData catalogue)
		=> Predict(student, gcses, asOf, catalogue, DfeTransitionMatrix.LoadDefault(), QualificationScale.Current);

	/// <summary>
	///     Predict from facts already projected to <see cref="GcseResult" />s using an explicit catalogue
	///     and qualification scale. The pipeline entry points materialise the GCSE list once for the engine
	///     and reuse it here, so the projection isn't repeated. <paramref name="asOf" /> is the reference
	///     date the student's age is computed against. The transition matrix defaults to the shipped
	///     extract; the engine threads an explicit, data-directory-sourced matrix through the overload below.
	/// </summary>
	public static StudentProfile Predict(
		StudentInput student, IReadOnlyList<GcseResult> gcses, DateOnly asOf, CatalogueData catalogue, QualificationScale scale)
		=> Predict(student, gcses, asOf, catalogue, DfeTransitionMatrix.LoadDefault(), scale);

	/// <summary>
	///     Predict using an explicit catalogue <em>and</em> transition matrix, so a host that ships its data
	///     in a non-default location drives prediction from that data rather than the process-global default.
	/// </summary>
	public static StudentProfile Predict(
		StudentInput student, IReadOnlyList<GcseResult> gcses, DateOnly asOf, CatalogueData catalogue, DfeTransitionMatrix matrix,
		QualificationScale scale)
	{
		var average = AverageGcseScore(gcses);
		var age = student.DateOfBirth is { } dob ? AgeCalculator.WholeYears(dob, asOf) : 0;

		return new(
			student.Id,
			average,
			[
				.. catalogue.Subjects.Select(subject =>
					new PredictedGrade(
						subject,
						Math.Max(
							catalogue.Meta(subject).Regression.Predict(average),
							BestEntryEquivalentPoints(student.PriorQualifications, subject, catalogue, scale)))),
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
