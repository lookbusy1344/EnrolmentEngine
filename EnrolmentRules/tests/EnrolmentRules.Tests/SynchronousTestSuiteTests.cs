namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Architecture guard for the test suite's synchronous execution boundary.</summary>
public sealed class SynchronousTestSuiteTests
{
	[Fact]
	public void test_sources_do_not_use_async_machinery_outside_approved_infrastructure()
	{
		var testDirectory = Path.Combine(Harness.RepoRoot, "tests", "EnrolmentRules.Tests");
		var violations = Directory.EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
			.Where(static file => !IsGeneratedOutput(file))
			.SelectMany(static file => SynchronousTestSuiteGuard.FindViolations(file, File.ReadAllText(file)))
			.ToArray();

		violations.Should().BeEmpty();
	}

	private static bool IsGeneratedOutput(string file)
	{
		var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return segments.Contains("bin") || segments.Contains("obj");
	}

	[Theory]
	[InlineData("public async Task Run() => await Work();")]
	[InlineData("public Task<int> Run() => Task.FromResult(1);")]
	[InlineData("public sealed class Fixture : IAsyncLifetime { }")]
	[InlineData("public void Run() => File.ReadAllTextAsync(\"input\");")]
	[InlineData("public void Run() { await using var stream = Open(); }")]
	public void incidental_async_unit_tests_remain_violations(string member)
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
	public void task_run_used_to_disguise_synchronous_work_remains_a_violation()
	{
		const string source = """
							  sealed class Example
							  {
							  	[UsesTestInfrastructure]
							  	public async Task Run()
							  	{
							  		await TestProcessHost.RunAsync("concurrent");
							  		await Task.Run(static () => { });
							  	}
							  }
							  """;

		SynchronousTestSuiteGuard.FindViolations("ExampleTests.cs", source)
			.Should()
			.Contain(static violation => violation.Contains("'Task'"));
	}

	[Fact]
	public void process_async_apis_are_allowed_inside_test_infrastructure()
	{
		const string source = """
							  namespace EnrolmentRules.Tests.TestInfrastructure;

							  sealed class Example
							  {
							  	public async Task RunAsync(Process process)
							  	{
							  		await process.StandardOutput.ReadToEndAsync();
							  		await process.WaitForExitAsync();
							  	}
							  }
							  """;

		SynchronousTestSuiteGuard.FindViolations("TestInfrastructure/Example.cs", source).Should().BeEmpty();
	}

	[Fact]
	public void async_marker_without_infrastructure_usage_does_not_create_a_blanket_exemption()
	{
		const string source = """
							  sealed class Example
							  {
							  	[UsesTestInfrastructure]
							  	public async Task Run()
							  	{
							  		await Task.Delay(1);
							  	}
							  }
							  """;

		SynchronousTestSuiteGuard.FindViolations("RuntimeAssetTests.cs", source).Should().NotBeEmpty();
	}

	[Fact]
	public void production_sources_remain_outside_every_test_only_exception()
	{
		const string source = """
							  sealed class Example
							  {
							  	[UsesTestInfrastructure]
							  	public async Task Run()
							  	{
							  		await TestProcessRunner.RunAsync("dotnet", [], ".");
							  	}
							  }
							  """;

		SynchronousTestSuiteGuard.FindViolations("src/EnrolmentRules.Engine/Example.cs", source).Should().NotBeEmpty();
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
	public void classifier_rejects_async_suffixed_method_declarations_outside_infrastructure()
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
	private const string ApprovedInfrastructureNamespace = "EnrolmentRules.Tests.TestInfrastructure";

	private static readonly HashSet<string> ForbiddenIdentifiers = [
		"Task",
		"IAsyncLifetime",
		"ReadAllTextAsync",
		"WriteAllTextAsync",
		"WaitForExitAsync",
		"DisposeAsync",
		"WaitAsync",
		"ForEachAsync",
		"ThrowAsync",
	];

	private static readonly HashSet<string> ApprovedInfrastructureTypes = [
		"TestProcessRunner",
		"TestProcessHost",
	];

	private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, HashSet<string>>> AllowedExternalMembers =
		new Dictionary<string, IReadOnlyDictionary<string, HashSet<string>>>(StringComparer.Ordinal) {
			["EligibilityShortCircuitTests.cs"] =
				new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) {
					["ValueTask"] = ["ExecuteAllRulesAsync", "ExecuteActionWorkflowAsync"],
					["ExecuteAllRulesAsync"] = ["ExecuteAllRulesAsync"],
					["ExecuteActionWorkflowAsync"] = ["ExecuteActionWorkflowAsync"],
				},
			["RatingEvaluatorTests.cs"] = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) {
				["ValueTask"] = ["ExecuteAllRulesAsync", "ExecuteActionWorkflowAsync"],
				["ExecuteAllRulesAsync"] = ["ExecuteAllRulesAsync"],
				["ExecuteActionWorkflowAsync"] = ["ExecuteActionWorkflowAsync"],
			},
			["StartupTests.cs"] = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["ExecuteAllRulesAsync"] = ["ExecuteAllRules"] },
		};

	public static IEnumerable<string> FindViolations(string fileName, string source)
	{
		var root = CSharpSyntaxTree.ParseText(source).GetRoot();
		var context = GuardContext.Create(fileName, root);
		var allowed = AllowedExternalMembers.GetValueOrDefault(context.FileName);

		foreach (var token in root.DescendantTokens()) {
			if ((token.IsKind(SyntaxKind.AsyncKeyword) || token.IsKind(SyntaxKind.AwaitKeyword))
				&& !IsAllowedAsyncToken(token, context)) {
				yield return Describe(context.FileName, token, token.ValueText);
			}
		}

		foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()) {
			var name = method.Identifier.ValueText;
			if (name.EndsWith("Async", StringComparison.Ordinal)
				&& !context.IsInfrastructure
				&& !IsAllowedMethodDeclaration(name, allowed)) {
				yield return Describe(context.FileName, method.Identifier, name);
			}
		}

		foreach (var identifier in root.DescendantNodes().OfType<SimpleNameSyntax>()) {
			var name = identifier.Identifier.ValueText;
			if (IsForbiddenIdentifier(identifier, name, context, allowed)) {
				yield return Describe(context.FileName, identifier.Identifier, name);
			}
		}
	}

	private static bool IsForbiddenIdentifier(
		SimpleNameSyntax identifier,
		string name,
		GuardContext context,
		IReadOnlyDictionary<string, HashSet<string>>? allowed)
	{
		if (context.IsInfrastructure) {
			return false;
		}

		if (name == "ValueTask") {
			return !IsAllowedExternalMember(identifier, name, allowed);
		}

		if (name.EndsWith("Async", StringComparison.Ordinal)) {
			return !IsAllowedAsyncIdentifier(identifier, context, allowed);
		}

		if (name == "Task") {
			return !IsAllowedTaskUsage(identifier, context);
		}

		return ForbiddenIdentifiers.Contains(name) && !IsAllowedAsyncIdentifier(identifier, context, allowed);
	}

	private static bool IsAllowedAsyncToken(SyntaxToken token, GuardContext context)
	{
		if (context.IsInfrastructure) {
			return true;
		}

		var enclosingMethod = token.Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
		return enclosingMethod is not null && IsApprovedAsyncTestMethod(enclosingMethod, context);
	}

	private static bool IsAllowedAsyncIdentifier(
		SimpleNameSyntax identifier,
		GuardContext context,
		IReadOnlyDictionary<string, HashSet<string>>? allowed)
	{
		if (IsAllowedExternalMember(identifier, identifier.Identifier.ValueText, allowed)) {
			return true;
		}

		var enclosingMethod = identifier.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
		if (enclosingMethod is null || !IsApprovedAsyncTestMethod(enclosingMethod, context)) {
			return false;
		}

		return identifier.Identifier.ValueText switch {
			"ThrowAsync" => true,
			var name when name.EndsWith("Async", StringComparison.Ordinal) => IsApprovedInfrastructureInvocation(identifier),
			_ => false,
		};
	}

	private static bool IsAllowedTaskUsage(SimpleNameSyntax identifier, GuardContext context)
	{
		var method = identifier.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
		return method is not null
			   && IsApprovedAsyncTestMethod(method, context)
			   && method.ReturnType.DescendantNodesAndSelf().Contains(identifier);
	}

	private static bool IsApprovedAsyncTestMethod(MethodDeclarationSyntax method, GuardContext context) =>
		context.IsTestProjectSource
		&& HasUsesTestInfrastructureAttribute(method)
		&& method.DescendantNodes().OfType<SimpleNameSyntax>().Any(IsApprovedInfrastructureReference);

	private static bool HasUsesTestInfrastructureAttribute(MethodDeclarationSyntax method) =>
		method.AttributeLists
			.SelectMany(static list => list.Attributes)
			.Any(static attribute => attribute.Name.ToString() is "UsesTestInfrastructure" or "UsesTestInfrastructureAttribute");

	private static bool IsApprovedInfrastructureReference(SimpleNameSyntax identifier) =>
		ApprovedInfrastructureTypes.Contains(identifier.Identifier.ValueText);

	private static bool IsApprovedInfrastructureInvocation(SimpleNameSyntax identifier) =>
		IsApprovedInfrastructureReference(identifier)
		|| (identifier.Parent is MemberAccessExpressionSyntax { Expression: SimpleNameSyntax expression }
			&& IsApprovedInfrastructureReference(expression));

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
		return $"{fileName}:{line}: forbidden '{construct}'";
	}

	private sealed record GuardContext(string FileName, bool IsInfrastructure, bool IsTestProjectSource)
	{
		public static GuardContext Create(string fileName, SyntaxNode root)
		{
			var shortFileName = Path.GetFileName(fileName);
			var namespaceName = root.DescendantNodes()
				.OfType<BaseNamespaceDeclarationSyntax>()
				.Select(static declaration => declaration.Name.ToString())
				.FirstOrDefault();
			var isInfrastructure = fileName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("TestInfrastructure")
								   || string.Equals(namespaceName, ApprovedInfrastructureNamespace, StringComparison.Ordinal);
			var isTestProjectSource = !fileName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("src");
			return new(shortFileName, isInfrastructure, isTestProjectSource);
		}
	}
}
