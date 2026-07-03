namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using EnrolmentRules.Extensions.DependencyInjection;
using Prediction;

public sealed class PublicApiSurfaceTests
{
	[Fact]
	public void equatable_collections_expose_no_implicit_conversion_operators()
	{
		var wrapperTypes = new[] {
			typeof(EquatableArray<int>),
			typeof(EquatableDictionary<string, int>),
		};

		var implicitOperators = wrapperTypes
			.SelectMany(static type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
			.Where(static method => method.Name == "op_Implicit")
			.Select(static method => method.ToString())
			.ToArray();

		implicitOperators.Should().BeEmpty("collection copying must remain explicit at the call site");
	}

	[Fact]
	public void public_surface_matches_the_design_spec()
	{
		var assemblies = new[] {
			(typeof(StudentInput).Assembly, new[] {
				"EnrolmentRules.Domain.AgeCalculator",
				"EnrolmentRules.Domain.ALevelGrade",
				"EnrolmentRules.Domain.AdjustmentKind",
				"EnrolmentRules.Domain.Adjustment",
				"EnrolmentRules.Domain.AdviceResult",
				"EnrolmentRules.Domain.BatchJsonContext",
				"EnrolmentRules.Domain.BatchOutcome",
				"EnrolmentRules.Domain.Catalogue",
				"EnrolmentRules.Domain.CatalogueData",
				"EnrolmentRules.Domain.CatalogueDataException",
				"EnrolmentRules.Domain.EnrolmentDataException",
				"EnrolmentRules.Domain.EnrolmentJsonContext",
				"EnrolmentRules.Domain.EnrolmentResult",
				"EnrolmentRules.Domain.EnrolmentSummary",
				"EnrolmentRules.Domain.EnumNames",
				"EnrolmentRules.Domain.EntryEquivalent",
				"EnrolmentRules.Domain.ExclusionPair",
				"EnrolmentRules.Domain.ExplainedResult",
				"EnrolmentRules.Domain.Explanation",
				"EnrolmentRules.Domain.EquatableArray",
				"EnrolmentRules.Domain.EquatableArray`1",
				"EnrolmentRules.Domain.EquatableDictionary`2",
				"EnrolmentRules.Domain.EquatableDictionaryFactory",
				"EnrolmentRules.Domain.GateAdvice",
				"EnrolmentRules.Domain.GcseResult",
				"EnrolmentRules.Domain.GcseSubjects",
				"EnrolmentRules.Domain.GradeChange",
				"EnrolmentRules.Domain.LintFinding",
				"EnrolmentRules.Domain.LintSeverity",
				"EnrolmentRules.Domain.PredictedGrade",
				"EnrolmentRules.Domain.PolicyFacts",
				"EnrolmentRules.Domain.PolicyThresholds",
				"EnrolmentRules.Domain.PolicyThresholdsException",
				"EnrolmentRules.Domain.PolicyThresholdsStore",
				"EnrolmentRules.Domain.PredictionModel",
				"EnrolmentRules.Domain.PredictionModel+Coefficients",
				"EnrolmentRules.Domain.Prerequisite",
				"EnrolmentRules.Domain.PrerequisiteSatisfaction",
				"EnrolmentRules.Domain.Qualification",
				"EnrolmentRules.Domain.QualificationScale",
				"EnrolmentRules.Domain.QualificationScaleEntry",
				"EnrolmentRules.Domain.QualificationScaleException",
				"EnrolmentRules.Domain.QualificationScaleStore",
				"EnrolmentRules.Domain.QualificationType",
				"EnrolmentRules.Domain.Rating",
				"EnrolmentRules.Domain.RatingExtensions",
				"EnrolmentRules.Domain.Recommendation",
				"EnrolmentRules.Domain.RestudyBar",
				"EnrolmentRules.Domain.StudentDocument",
				"EnrolmentRules.Domain.StudentInput",
				"EnrolmentRules.Domain.StudentProfile",
				"EnrolmentRules.Domain.StudentValidator",
				"EnrolmentRules.Domain.Subject",
				"EnrolmentRules.Domain.SubjectAdvice",
				"EnrolmentRules.Domain.SubjectExclusion",
				"EnrolmentRules.Domain.SubjectJsonConverter",
				"EnrolmentRules.Domain.SubjectMeta",
				"EnrolmentRules.Domain.Thresholds",
				"EnrolmentRules.Domain.TransitionEvidence",
				"EnrolmentRules.Domain.ValidationOutcome",
				"EnrolmentRules.Domain.ValidatedEvaluation`1",
				"EnrolmentRules.Domain.YamlConverter",
			}),
			(typeof(GradePredictor).Assembly, new[] {
				"EnrolmentRules.Prediction.DfeTransitionMatrix",
				"EnrolmentRules.Prediction.GradePredictor",
				"EnrolmentRules.Prediction.TransitionMatrixException",
			}),
			(typeof(IEnrolmentEngine).Assembly, new[] {
				"EnrolmentRules.Engine.Authoring.CatalogueException",
				"EnrolmentRules.Engine.Authoring.CatalogueStore",
				"EnrolmentRules.Engine.Authoring.WorkflowException",
				"EnrolmentRules.Engine.Authoring.WorkflowLintException",
				"EnrolmentRules.Engine.Authoring.WorkflowLinter",
				"EnrolmentRules.Engine.Authoring.WorkflowProbeException",
				"EnrolmentRules.Engine.Authoring.WorkflowSchemaException",
				"EnrolmentRules.Engine.Authoring.WorkflowStore",
				"EnrolmentRules.Engine.EnrolmentEngine",
				"EnrolmentRules.Engine.IEnrolmentAdvisor",
				"EnrolmentRules.Engine.IEnrolmentEngine",
				"EnrolmentRules.Engine.IEnrolmentEngineFactory",
				"EnrolmentRules.Engine.IEnrolmentEvaluator",
				"EnrolmentRules.Engine.Hosting.DirectoryDataSource",
				"EnrolmentRules.Engine.Hosting.EnrolmentEngineFactory",
				"EnrolmentRules.Engine.Hosting.IEnrolmentDataSource",
				"EnrolmentRules.Engine.Hosting.WorkflowContent",
			}),
			(typeof(ServiceCollectionExtensions).Assembly, new[] {
				"EnrolmentRules.Extensions.DependencyInjection.EnrolmentEngineOptions",
				"EnrolmentRules.Extensions.DependencyInjection.ServiceCollectionExtensions",
			}),
		};

		foreach (var (assembly, expected) in assemblies) {
			var actual = assembly.GetExportedTypes()
				.Select(static type => type.FullName)
				.OrderBy(static name => name, StringComparer.Ordinal)
				.ToArray();

			var expectedSorted = expected.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
			var missing = expectedSorted.Except(actual, StringComparer.Ordinal).ToArray();
			var extra = actual.Except(expectedSorted, StringComparer.Ordinal).ToArray();

			var parts = new List<string>(2);
			if (missing.Length > 0) {
				parts.Add($"Missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
			}

			if (extra.Length > 0) {
				parts.Add($"Extra:{Environment.NewLine}{string.Join(Environment.NewLine, extra)}");
			}

			(missing.Length, extra.Length).Should().Be((0, 0), string.Join(Environment.NewLine, parts));
		}
	}
}
