namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Guards against drift between the compiled GCSE input vocabulary and the catalogue / example
///     student documents.
/// </summary>
public sealed class GcseVocabularyTests
{
	private static string ExamplesDir => Path.Combine(Harness.RepoRoot, "examples");

	[Fact]
	public void shipped_catalogue_covers_every_gcse_vocabulary_key()
	{
		var act = () => CatalogueStore.LoadAndValidate(Harness.DataDir);

		act.Should().NotThrow();
		GcseSubjects.ValidateCatalogueCoverage(Harness.Catalogue.Subjects);
	}

	[Fact]
	public void catalogue_store_rejects_a_catalogue_missing_a_gcse_vocabulary_subject()
	{
		var act = () => CatalogueStore.LoadAndValidate(CatalogueTests.AllSubjectsFixtureDirectory("art"));

		act.Should().Throw<CatalogueException>().WithMessage("*GCSE vocabulary key 'art'*");
	}

	[Fact]
	public void example_student_documents_use_recognised_gcse_keys()
	{
		var unknownKeys = new List<string>();
		foreach (var (path, student) in ExampleStudents()) {
			foreach (var key in student.Gcses?.Keys ?? []) {
				if (!GcseSubjects.IsKnown(key)) {
					unknownKeys.Add($"{path}: '{key}'");
				}
			}
		}

		unknownKeys.Should().BeEmpty("every GCSE key in examples/ must be in GcseSubjects.Known");
	}

	private static IEnumerable<(string Path, StudentInput Student)> ExampleStudents()
	{
		foreach (var path in Directory.EnumerateFiles(ExamplesDir, "*.*", SearchOption.AllDirectories)
					 .Where(static path => Path.GetExtension(path) is ".json" or ".yaml" or ".yml")
					 .OrderBy(static path => path, StringComparer.Ordinal)) {
			if (Path.GetFileName(path).EndsWith(".expected.json", StringComparison.Ordinal)
				|| Path.GetFileName(path).EndsWith(".append.yaml", StringComparison.Ordinal)) {
				continue;
			}

			var document = LoadStudentDocument(path);
			if (document is { } loaded) {
				yield return (path, loaded.Student);
			}
		}
	}

	private static StudentDocument? LoadStudentDocument(string path) =>
		Path.GetExtension(path) switch {
			".json" => JsonSerializer.Deserialize(File.ReadAllText(path), EnrolmentJsonContext.Default.StudentDocument),
			".yaml" or ".yml" => YamlConverter.ToJsonNode(File.ReadAllText(path))
				.Deserialize(EnrolmentJsonContext.Default.StudentDocument),
			_ => null,
		};
}
