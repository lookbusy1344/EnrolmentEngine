namespace EnrolmentRules.Tests;

using AwesomeAssertions;

/// <summary>
///     Production bootstrap must enforce semantic workflow lint, not just schema validation and probe
///     compilation. These tests drive the public engine/factory entry points rather than the linter alone.
/// </summary>
public sealed class WorkflowBootstrapLintTests
{
	[Fact]
	public void create_rejects_amber_before_green()
	{
		var fixture = CopyShippedLayout();
		try {
			RewriteSubjectRatings(
				fixture,
				static content => content.Replace(
					"""
					  - RuleName: 'maths:green'
					    SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
					    Expression: >-
					      facts.Gcse("maths") >= facts.TopEntry && facts.Predicted("maths") >= ALevelGrade.A && facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.A) >= facts.MinDfeGreenProbabilityAtOrAbove
					  - RuleName: 'maths:amber'
					    SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
					    Expression: >-
					      facts.Gcse("maths") >= facts.TopEntry && facts.Predicted("maths") >= ALevelGrade.B && facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.B) >= facts.MinDfeAmberProbabilityAtOrAbove
					""",
					"""
					  - RuleName: 'maths:amber'
					    SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
					    Expression: >-
					      facts.Gcse("maths") >= facts.TopEntry && facts.Predicted("maths") >= ALevelGrade.B && facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.B) >= facts.MinDfeAmberProbabilityAtOrAbove
					  - RuleName: 'maths:green'
					    SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
					    Expression: >-
					      facts.Gcse("maths") >= facts.TopEntry && facts.Predicted("maths") >= ALevelGrade.A && facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.A) >= facts.MinDfeGreenProbabilityAtOrAbove
					""",
					StringComparison.Ordinal));

			var act = () => EnrolmentEngine.Create(WorkflowsDir(fixture), DataDir(fixture), Harness.AsOf);

			act.Should().Throw<WorkflowLintException>()
				.WithMessage("*maths rules must be ordered green → amber → red*");
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public void create_rejects_a_missing_subject_tier()
	{
		var fixture = CopyShippedLayout();
		try {
			RewriteSubjectRatings(
				fixture,
				static content => content.Replace(
					"""
					  - RuleName: 'maths:amber'
					    SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
					    Expression: >-
					      facts.Gcse("maths") >= facts.TopEntry && facts.Predicted("maths") >= ALevelGrade.B && facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.B) >= facts.MinDfeAmberProbabilityAtOrAbove
					""",
					string.Empty,
					StringComparison.Ordinal));

			var act = () => EnrolmentEngine.Create(WorkflowsDir(fixture), DataDir(fixture), Harness.AsOf);

			act.Should().Throw<WorkflowLintException>()
				.WithMessage("*maths is missing its amber rule*");
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public void create_rejects_a_duplicate_subject_tier()
	{
		var fixture = CopyShippedLayout();
		try {
			RewriteSubjectRatings(
				fixture,
				static content => content.Replace(
					"""
					  - RuleName: 'maths:red'
					""",
					"""
					  - RuleName: 'maths:amber'
					    SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
					    Expression: >-
					      facts.Gcse("maths") >= facts.TopEntry && facts.Predicted("maths") >= ALevelGrade.B && facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.B) >= facts.MinDfeAmberProbabilityAtOrAbove
					  - RuleName: 'maths:red'
					""",
					StringComparison.Ordinal));

			var act = () => EnrolmentEngine.Create(WorkflowsDir(fixture), DataDir(fixture), Harness.AsOf);

			act.Should().Throw<WorkflowLintException>()
				.WithMessage("*maths has 2 amber rules*");
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public void create_rejects_a_conditional_red_rule()
	{
		var fixture = CopyShippedLayout();
		try {
			RewriteSubjectRatings(
				fixture,
				static content => content.Replace(
					"""
					  - RuleName: 'maths:red'
					    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
					    Expression: >-
					      true
					""",
					"""
					  - RuleName: 'maths:red'
					    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
					    Expression: >-
					      facts.Predicted("maths") < ALevelGrade.C
					""",
					StringComparison.Ordinal));

			var act = () => EnrolmentEngine.Create(WorkflowsDir(fixture), DataDir(fixture), Harness.AsOf);

			act.Should().Throw<WorkflowLintException>()
				.WithMessage("*maths:red must be an unconditional true catch-all*");
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public void create_rejects_an_unknown_subject_key_that_still_compiles()
	{
		var fixture = CopyShippedLayout();
		try {
			RewriteSubjectRatings(
				fixture,
				static content => content.Replace("facts.Predicted(\"maths\") >= ALevelGrade.A", "facts.Predicted(\"mathz\") >= ALevelGrade.A",
					StringComparison.Ordinal));

			var act = () => EnrolmentEngine.Create(WorkflowsDir(fixture), DataDir(fixture), Harness.AsOf);

			act.Should().Throw<WorkflowLintException>()
				.WithMessage("*unknown subject key 'mathz'*");
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public void failed_lint_reload_preserves_current_and_later_clean_reload_succeeds()
	{
		var fixture = CopyShippedLayout();
		try {
			var ratingsPath = Path.Combine(WorkflowsDir(fixture), "subject-ratings.yaml");
			var clean = File.ReadAllText(ratingsPath);
			using var factory = EnrolmentEngineFactory.Create(WorkflowsDir(fixture), DataDir(fixture), Harness.AsOf);
			var before = factory.Current;

			File.WriteAllText(
				ratingsPath,
				clean.Replace(
					"""
					  - RuleName: 'maths:red'
					    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
					    Expression: >-
					      true
					""",
					"""
					  - RuleName: 'maths:red'
					    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
					    Expression: >-
					      facts.Predicted("maths") < ALevelGrade.C
					""",
					StringComparison.Ordinal));

			var act = () => factory.Reload();
			act.Should().Throw<WorkflowLintException>();
			factory.Current.Should().BeSameAs(before);

			File.WriteAllText(ratingsPath, clean);
			factory.Reload();

			factory.Current.Should().NotBeSameAs(before);
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	private static string CopyShippedLayout()
	{
		var fixture = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		CopyTree(Harness.WorkflowsDir, WorkflowsDir(fixture));
		CopyTree(Harness.DataDir, DataDir(fixture));
		return fixture;
	}

	private static void RewriteSubjectRatings(string fixture, Func<string, string> rewrite)
	{
		var path = Path.Combine(WorkflowsDir(fixture), "subject-ratings.yaml");
		File.WriteAllText(path, rewrite(File.ReadAllText(path)));
	}

	private static string WorkflowsDir(string fixture) => Path.Combine(fixture, "workflows");

	private static string DataDir(string fixture) => Path.Combine(fixture, "data");

	private static void CopyTree(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			var relative = Path.GetRelativePath(source, file);
			var target = Path.Combine(destination, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, true);
		}
	}
}
