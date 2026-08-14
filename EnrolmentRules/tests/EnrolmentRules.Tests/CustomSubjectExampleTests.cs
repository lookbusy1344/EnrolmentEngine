namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Smoke test for <c>examples/custom-subject/</c>: merges the committed append snippets into a copy
///     of the shipped catalogue/workflows, boots a real engine over the result, and evaluates the
///     committed sample student. This is what stopped the example silently staling before (its subject
///     name collided with the shipped catalogue and would have failed lint/startup on merge, but nothing
///     exercised the merge) — a future addition to the shipped catalogue that reintroduces the collision
///     now fails this test instead of only being discovered by a reader following the README by hand.
/// </summary>
public sealed class CustomSubjectExampleTests
{
	private static readonly Subject Philosophy = Subject.Parse("philosophy");

	private static string ExampleDir => Path.Combine(Harness.RepoRoot, "examples", "custom-subject");

	[Fact]
	public void the_committed_example_subject_is_not_already_shipped()
	{
		// The defect this test suite guards against: the example must name a subject absent from the
		// shipped catalogue, or merging it duplicates an existing entry and fails lint/startup instead of
		// demonstrating the data-only extension path.
		Harness.Catalogue.Subjects.Should().NotContain(Philosophy);
	}

	[Fact]
	public void the_custom_subject_example_merges_and_rates_the_sample_student_green()
	{
		var (workflowsDir, dataDir) = MergedFixture();
		try {
			var engine = EnrolmentEngine.Create(workflowsDir, dataDir, Harness.AsOf);
			engine.Catalogue.Subjects.Should().Contain(Philosophy);

			var result = engine.Evaluate(ReadSampleStudent());

			result.Recommendations.Should().ContainSingle(recommendation => recommendation.Subject == Philosophy)
				  .Which.Rating.Should().Be(Rating.Green);
		}
		finally {
			Directory.Delete(Path.GetDirectoryName(workflowsDir)!, true);
		}
	}

	private static (string WorkflowsDir, string DataDir) MergedFixture()
	{
		var root = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "custom-subject-" + Guid.NewGuid().ToString("N"));
		var dataDir = CopyDirectory(Harness.DataDir, Path.Combine(root, "data"));
		var workflowsDir = CopyDirectory(Harness.WorkflowsDir, Path.Combine(root, "workflows"));

		AppendSnippet(
			Path.Combine(dataDir, CatalogueStore.CatalogueFileName),
			Path.Combine(ExampleDir, "data", "catalogue.append.yaml"));
		AppendSnippet(
			Path.Combine(workflowsDir, "subject-ratings.yaml"),
			Path.Combine(ExampleDir, "workflows", "subject-ratings.append.yaml"));

		return (workflowsDir, dataDir);
	}

	private static string CopyDirectory(string source, string destination)
	{
		foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) {
			Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
		}

		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal));
		}

		return destination;
	}

	private static void AppendSnippet(string basePath, string snippetPath) =>
		File.WriteAllText(basePath, File.ReadAllText(basePath).TrimEnd('\n') + "\n" + File.ReadAllText(snippetPath));

	private static StudentInput ReadSampleStudent()
	{
		var json = File.ReadAllText(Path.Combine(ExampleDir, "student.json"));
		var document = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentDocument);
		return document!.Student;
	}
}
