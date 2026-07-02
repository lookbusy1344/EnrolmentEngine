namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Architecture guard for the test suite's synchronous execution boundary.</summary>
public sealed class SynchronousTestSuiteTests
{
	[Fact]
	public void test_sources_do_not_use_async_machinery()
	{
		var testDirectory = Path.Combine(Harness.RepoRoot, "tests", "EnrolmentRules.Tests");
		var violations = Directory.EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
			.Where(static file => !IsGeneratedOutput(file))
			.SelectMany(static file => SynchronousTestSuiteGuard.FindViolations(file, File.ReadAllText(file)))
			.ToArray();

		violations.Should().BeEmpty();
	}

	// The recursive sweep must skip build output (bin/obj): source-generated JSON, AssemblyInfo, and
	// other MSBuild-emitted .cs files legitimately contain Task/async and are not hand-written tests.
	private static bool IsGeneratedOutput(string file)
	{
		var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return segments.Contains("bin") || segments.Contains("obj");
	}

	[Theory]
	[InlineData("public async Task Run() => await Work();")]
	[InlineData("public Task<int> Run() => Task.FromResult(1);")]
	[InlineData("public sealed class Fixture : IAsyncLifetime { }")]
	[InlineData("public void Run() => Task.WhenAll();")]
	[InlineData("public void Run() => File.ReadAllTextAsync(\"input\");")]
	[InlineData("public void Run() { await using var stream = Open(); }")]
	public void classifier_rejects_representative_async_constructs(string member)
	{
		var source = $$"""
			sealed class Example
			{
				{{member}}
			}
			""";

		SynchronousTestSuiteGuard.FindViolations("ExampleTests.cs", source).Should().NotBeEmpty();
	}

	[Fact]
	public void classifier_allows_required_completed_rules_engine_members()
	{
		const string source = """
			sealed class EngineDouble
			{
				public ValueTask<int> ExecuteAllRulesAsync() => ValueTask.FromResult(1);
				public ValueTask<int> ExecuteActionWorkflowAsync() => ValueTask.FromResult(1);
			}
			""";

		SynchronousTestSuiteGuard.FindViolations("RatingEvaluatorTests.cs", source).Should().BeEmpty();
	}

	[Fact]
	public void classifier_rejects_value_task_outside_an_allowlisted_interface_member()
	{
		const string source = "public ValueTask<int> ArbitraryHelper() => ValueTask.FromResult(1);";

		SynchronousTestSuiteGuard.FindViolations("RatingEvaluatorTests.cs", source).Should().NotBeEmpty();
	}

	[Fact]
	public void classifier_rejects_async_suffixed_method_declarations()
	{
		const string source = "public sealed class Example { public void ArbitraryHelperAsync() { } }";

		SynchronousTestSuiteGuard.FindViolations("ExampleTests.cs", source).Should().NotBeEmpty();
	}

	[Fact]
	public void classifier_rejects_rules_engine_calls_outside_an_allowlisted_bridge()
	{
		const string source = "public void ArbitraryHelper() => engine.ExecuteAllRulesAsync();";

		SynchronousTestSuiteGuard.FindViolations("RatingEvaluatorTests.cs", source).Should().NotBeEmpty();
	}
}

internal static class SynchronousTestSuiteGuard
{
	private static readonly HashSet<string> ForbiddenIdentifiers =
	[
		"Task",
		"IAsyncLifetime",
		"ReadAllTextAsync",
		"WriteAllTextAsync",
		"WaitForExitAsync",
		"DisposeAsync",
		"WaitAsync",
		"ForEachAsync",
	];

	private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, HashSet<string>>> AllowedExternalMembers =
		new Dictionary<string, IReadOnlyDictionary<string, HashSet<string>>>(StringComparer.Ordinal) {
			["EligibilityShortCircuitTests.cs"] = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) {
				["ValueTask"] = ["ExecuteAllRulesAsync", "ExecuteActionWorkflowAsync"],
				["ExecuteAllRulesAsync"] = ["ExecuteAllRulesAsync"],
				["ExecuteActionWorkflowAsync"] = ["ExecuteActionWorkflowAsync"],
			},
			["RatingEvaluatorTests.cs"] = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) {
				["ValueTask"] = ["ExecuteAllRulesAsync", "ExecuteActionWorkflowAsync"],
				["ExecuteAllRulesAsync"] = ["ExecuteAllRulesAsync"],
				["ExecuteActionWorkflowAsync"] = ["ExecuteActionWorkflowAsync"],
			},
			["StartupTests.cs"] = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) {
				["ExecuteAllRulesAsync"] = ["ExecuteAllRules"],
			},
		};

	public static IEnumerable<string> FindViolations(string fileName, string source)
	{
		var root = CSharpSyntaxTree.ParseText(source).GetRoot();
		var allowed = AllowedExternalMembers.GetValueOrDefault(Path.GetFileName(fileName));

		foreach (var token in root.DescendantTokens()) {
			if (token.IsKind(SyntaxKind.AsyncKeyword) || token.IsKind(SyntaxKind.AwaitKeyword)) {
				yield return Describe(fileName, token, token.ValueText);
			}
		}

		foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()) {
			var name = method.Identifier.ValueText;
			if (name.EndsWith("Async", StringComparison.Ordinal) && !IsAllowedMethodDeclaration(name, allowed)) {
				yield return Describe(fileName, method.Identifier, name);
			}
		}

		foreach (var identifier in root.DescendantNodes().OfType<SimpleNameSyntax>()) {
			var name = identifier.Identifier.ValueText;
			if ((ForbiddenIdentifiers.Contains(name) || name.EndsWith("Async", StringComparison.Ordinal) || name == "ValueTask")
				&& !IsAllowedExternalMember(identifier, name, allowed)) {
				yield return Describe(fileName, identifier.Identifier, name);
			}
		}
	}

	private static bool IsAllowedMethodDeclaration(
		string name,
		IReadOnlyDictionary<string, HashSet<string>>? allowed) =>
		allowed is not null
		&& allowed.TryGetValue(name, out var allowedMethods)
		&& allowedMethods.Contains(name);

	private static bool IsAllowedExternalMember(
		SimpleNameSyntax identifier,
		string name,
		IReadOnlyDictionary<string, HashSet<string>>? allowed)
	{
		if (allowed is null || !allowed.TryGetValue(name, out var allowedMethods)) {
			return false;
		}

		return identifier.AncestorsAndSelf()
			.OfType<MethodDeclarationSyntax>()
			.Any(method => allowedMethods.Contains(method.Identifier.ValueText));
	}

	private static string Describe(string fileName, SyntaxToken token, string construct)
	{
		var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
		return $"{Path.GetFileName(fileName)}:{line}: forbidden '{construct}'";
	}
}
