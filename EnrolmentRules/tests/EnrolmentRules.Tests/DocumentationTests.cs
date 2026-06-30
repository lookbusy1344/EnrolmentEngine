namespace EnrolmentRules.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Engine;

/// <summary>Guards local cross-references in the maintained project guides.</summary>
public sealed partial class DocumentationTests
{
	private static readonly string[] MaintainedDocuments = [
		"README.md",
		Path.Combine("docs", "technical-reference.md"),
		Path.Combine("docs", "walkthrough.md"),
		Path.Combine("docs", "rule-authoring.md"),
	];

	/// <summary>Transient planning notes that may be deleted at any time; links into them are not validated.</summary>
	private static readonly string TransientPlansDirectory =
		Path.GetFullPath(Path.Combine("docs", "plans"), Harness.RepoRoot);

	[Fact]
	public void local_markdown_links_resolve_to_existing_files_and_anchors()
	{
		var failures = MaintainedDocuments
			.SelectMany(ValidateLinks)
			.ToArray();

		failures.Should().BeEmpty();
	}

	[Fact]
	public void enrolment_engine_has_no_public_constructors()
	{
		typeof(EnrolmentEngine).GetConstructors(BindingFlags.Instance | BindingFlags.Public)
			.Should()
			.BeEmpty("library hosts should construct via CreateAsync or DI, not new EnrolmentEngine(...)");
	}

	[Fact]
	public void technical_reference_validated_evaluation_example_uses_the_current_api_shape()
	{
		var technicalReference = File.ReadAllText(
			Path.Combine(Harness.RepoRoot, "docs", "technical-reference.md"));

		technicalReference.Should().Contain("validated.Validation.IsValid");
		technicalReference.Should().Contain("validated.Validation.Errors");
		technicalReference.Should().NotContain("validated.IsValid");
		technicalReference.Should().NotContain("validated.Outcome");
	}

	private static IEnumerable<string> ValidateLinks(string relativePath)
	{
		var sourcePath = Path.Combine(Harness.RepoRoot, relativePath);
		var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
		foreach (Match match in MarkdownLink().Matches(File.ReadAllText(sourcePath))) {
			var destination = match.Groups[1].Value;
			if (Uri.TryCreate(destination, UriKind.Absolute, out _)) {
				continue;
			}

			var parts = destination.Split('#', 2);
			var targetPath = parts[0].Length == 0 ? sourcePath : Path.GetFullPath(parts[0], sourceDirectory);
			if (targetPath.StartsWith(TransientPlansDirectory, StringComparison.Ordinal)) {
				continue;
			}

			if (!File.Exists(targetPath)) {
				yield return $"{relativePath}: missing link target '{destination}'";
				continue;
			}

			if (parts.Length == 2 && !Anchors(targetPath).Contains(parts[1])) {
				yield return $"{relativePath}: missing anchor '{destination}'";
			}
		}
	}

	private static HashSet<string> Anchors(string path) =>
		File.ReadLines(path)
			.Select(static line => Heading().Match(line))
			.Where(static match => match.Success)
			.Select(static match => Slug(match.Groups[1].Value))
			.ToHashSet(StringComparer.Ordinal);

	private static string Slug(string heading) =>
		string.Concat(heading.Trim().ToLowerInvariant()
				.Where(static character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_'))
			.Replace(' ', '-');

	[GeneratedRegex(@"\[[^\]]+\]\((?!https?://|mailto:)([^)]+)\)", RegexOptions.CultureInvariant)]
	private static partial Regex MarkdownLink();

	[GeneratedRegex(@"^#{1,6}\s+(.+?)\s*$", RegexOptions.CultureInvariant)]
	private static partial Regex Heading();
}
