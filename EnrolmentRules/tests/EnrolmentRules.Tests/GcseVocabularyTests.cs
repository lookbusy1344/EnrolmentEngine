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
	public void shipped_catalogue_happens_to_cover_every_gcse_vocabulary_key()
	{
		// Not an enforced invariant (Elite auxiliary policy plan step 1.4 decouples the two vocabularies —
		// GcseSubjects.Known is the input vocabulary and a catalogue may legitimately offer only a subset
		// of it as A-levels), just a documented fact about the shipped Standard catalogue today.
		var offered = Harness.Catalogue.Subjects.Select(static s => EnumNames.NameOf(s)).ToHashSet(StringComparer.Ordinal);

		GcseSubjects.Known.Should().BeSubsetOf(offered);
	}

	[Fact]
	public void catalogue_store_accepts_a_catalogue_missing_a_gcse_vocabulary_subject()
	{
		// The reduced catalogue (every subject except Art) still recognises the "art" GCSE key as valid
		// student input — GcseSubjects.Known is not coupled to which A-levels a policy's catalogue offers.
		var catalogue = CatalogueStore.LoadAndValidate(CatalogueTests.AllSubjectsFixtureDirectory("art"));

		catalogue.Subjects.Should().NotContain(Subject.Art);
		GcseSubjects.IsKnown("art").Should().BeTrue();

		var errors = StudentValidator.Validate(
			new("S", new Dictionary<string, int> {
				["art"] = 7,
				["maths"] = 6,
			}, []) {
				DateOfBirth = new(2009, 9, 1),
			},
			catalogue,
			Harness.Scale);

		errors.Should().BeEmpty();
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
			if (document is not null) {
				yield return (path, document.Student);
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
