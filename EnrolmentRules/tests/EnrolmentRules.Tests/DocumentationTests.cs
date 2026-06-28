namespace EnrolmentRules.Tests;

using System.Text.RegularExpressions;
using FluentAssertions;

/// <summary>Guards executable commands and local cross-references in the maintained project guides.</summary>
public sealed partial class DocumentationTests
{
	private static readonly string[] MaintainedDocuments = [
		"README.md",
		Path.Combine("docs", "walkthrough.md"),
		Path.Combine("docs", "rule-authoring.md"),
	];
	private static readonly string[] ContributorGuides = [
		Path.Combine("docs", "walkthrough.md"),
		Path.Combine("docs", "rule-authoring.md"),
	];

	[Fact]
	public void dotnet_test_commands_are_bounded_by_gtimeout()
	{
		var unbounded = ContributorGuides
			.SelectMany(ReadLines)
			.Where(static line => DotnetTestCommand().IsMatch(line) && !line.Contains("gtimeout", StringComparison.Ordinal))
			.ToArray();

		unbounded.Should().BeEmpty("every test invocation must have a bounded runtime");
	}

	[Fact]
	public void local_markdown_links_resolve_to_existing_files_and_anchors()
	{
		var failures = MaintainedDocuments
			.SelectMany(ValidateLinks)
			.ToArray();

		failures.Should().BeEmpty();
	}

	private static IEnumerable<string> ReadLines(string relativePath) =>
		File.ReadLines(Path.Combine(Harness.RepoRoot, relativePath));

	/// <summary>Transient planning notes that may be deleted at any time; links into them are not validated.</summary>
	private static readonly string TransientPlansDirectory =
		Path.GetFullPath(Path.Combine("docs", "plans"), Harness.RepoRoot);

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

	[GeneratedRegex(@"^\s*(?:\$\s*)?dotnet\s+test\b", RegexOptions.CultureInvariant)]
	private static partial Regex DotnetTestCommand();

	[GeneratedRegex(@"\[[^\]]+\]\((?!https?://|mailto:)([^)]+)\)", RegexOptions.CultureInvariant)]
	private static partial Regex MarkdownLink();

	[GeneratedRegex(@"^#{1,6}\s+(.+?)\s*$", RegexOptions.CultureInvariant)]
	private static partial Regex Heading();
}
