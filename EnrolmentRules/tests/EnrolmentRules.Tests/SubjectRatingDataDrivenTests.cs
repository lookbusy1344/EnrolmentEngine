namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using Prediction;

/// <summary>
///     Phase 3 follow-on — prove that adding a subject is an end-to-end data exercise, not just a catalogue
///     parse trick: a workflow/catalogue fixture introducing Philosophy must produce both a prediction and a final
///     recommendation for it.
/// </summary>
public sealed class SubjectRatingDataDrivenTests
{
	[Fact]
	public async Task adding_a_subject_in_data_only_flows_through_prediction_and_final_recommendations()
	{
		var fixture = WriteFixture();
		try {
			var dataDir = Path.Combine(fixture, "data");
			var workflowsDir = Path.Combine(fixture, "workflows");
			var catalogue = CatalogueStore.LoadAndValidate(dataDir);
			var workflows = WorkflowStore.LoadAndValidate(workflowsDir, Path.Combine(workflowsDir, WorkflowStore.SchemaFileName));
			var thresholds = PolicyThresholdsStore.LoadAndValidate(Harness.DataDir);
			var probeStudent = new StudentInput(
				"probe",
				new Dictionary<string, int> {
					["english_language"] = thresholds.TopEntry,
					["maths"] = thresholds.TopEntry,
					["history"] = thresholds.TopEntry,
					["art"] = thresholds.TopEntry,
					["biology"] = thresholds.TopEntry,
				},
				[]);
			var probeGcses = probeStudent.ToGcseResults();
			var probeProfile = GradePredictor.Predict(probeStudent, probeGcses, Harness.AsOf, catalogue, QualificationScale.Default);
			var engine = WorkflowStore.BuildEngine(workflows);
			await WorkflowStore.ProbeCompileAsync(
				engine,
				workflows,
				[
					.. RatingEvaluator.EligibilityParameters(probeGcses, thresholds),
					new("facts", new RatingFacts(probeProfile, probeGcses, new(thresholds), catalogue, QualificationScale.Default)),
				]);

			var subject = new Subject("philosophy");
			var student = new StudentInput(
				"S-PHILOSOPHY",
				new Dictionary<string, int> {
					["english_language"] = 8,
					["maths"] = 8,
					["history"] = 8,
					["art"] = 8,
					["physics"] = 8,
				},
				[]);

			var profile = GradePredictor.Predict(student, student.ToGcseResults(), Harness.AsOf, catalogue, QualificationScale.Default);
			var result = await new EnrolmentEngine(engine, thresholds, catalogue, Harness.AsOf).EvaluateAsync(student);

			profile.PredictedGrades.Select(static grade => grade.Subject).Should().Contain(subject);
			result.Recommendations.Should().HaveCount(catalogue.Subjects.Count);
			result.Recommendations.Should()
				.ContainSingle(recommendation => recommendation.Subject == subject && recommendation.Rating == Rating.Green);
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	private static string WriteFixture()
	{
		var dir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "phase3-subject-" + Guid.NewGuid().ToString("N"));
		var dataDir = Path.Combine(dir, "data");
		var workflowsDir = Path.Combine(dir, "workflows");
		Directory.CreateDirectory(dataDir);
		Directory.CreateDirectory(workflowsDir);

		File.Copy(Path.Combine(Harness.DataDir, CatalogueStore.SchemaFileName), Path.Combine(dataDir, CatalogueStore.SchemaFileName));
		File.Copy(Path.Combine(Harness.WorkflowsDir, WorkflowStore.SchemaFileName), Path.Combine(workflowsDir, WorkflowStore.SchemaFileName));
		File.Copy(Path.Combine(Harness.WorkflowsDir, "eligibility.yaml"), Path.Combine(workflowsDir, "eligibility.yaml"));

		File.WriteAllText(
			Path.Combine(dataDir, CatalogueStore.CatalogueFileName),
			File.ReadAllText(Path.Combine(Harness.DataDir, CatalogueStore.CatalogueFileName))
			+ """

			    - subject: philosophy
			      ucas_weight: 60
			      regression: { slope: 0.90, intercept: -1.00 }
			  """);

		File.WriteAllText(
			Path.Combine(workflowsDir, "subject-ratings.yaml"),
			File.ReadAllText(Path.Combine(Harness.WorkflowsDir, "subject-ratings.yaml"))
			+ """

			    - RuleName: 'philosophy:green'
			      SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
			      Expression: >-
			        facts.Average >= facts.HumanitiesAverageEntry && facts.Predicted("philosophy") >= ALevelGrade.B
			    - RuleName: 'philosophy:amber'
			      SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
			      Expression: >-
			        facts.Average >= facts.HumanitiesAverageEntry && facts.Predicted("philosophy") >= ALevelGrade.C
			    - RuleName: 'philosophy:red'
			      SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
			      Expression: >-
			        true
			  """);

		return dir;
	}
}
