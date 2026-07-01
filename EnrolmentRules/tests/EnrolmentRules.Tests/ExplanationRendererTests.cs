namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Cli;
using Domain;

/// <summary>
///     Human-readable explanation rendering. The renderer is a pure projection of an existing
///     <see cref="ExplainedResult" />; these tests lock the markdown shape, the ineligible header and
///     the fact that a prebuilt result can be rendered directly without re-evaluation.
/// </summary>
public sealed class ExplanationRendererTests
{
	[Fact]
	public async Task cli_explain_text_matches_the_committed_markdown_golden()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "golden", "strong-constraints.json");
		var expectedPath = Path.Combine(Harness.RepoRoot, "examples", "golden", "strong-constraints.expected.md");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--explain-text", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();
		File.Exists(expectedPath).Should().BeTrue();

		var expected = await File.ReadAllTextAsync(expectedPath);
		stdout.ToString().ReplaceLineEndings().TrimEnd()
			.Should().Be(expected.ReplaceLineEndings().TrimEnd());
	}

	[Fact]
	public async Task ineligible_explanations_render_the_gate_reasons()
	{
		var engine = await Harness.ShippedEngineAsync();
		var explained = engine.Explain(new("S-INELIGIBLE", new Dictionary<string, int> { ["maths"] = 6 }, []));
		await using var stdout = new StringWriter();

		ExplanationRenderer.Render(explained, stdout);

		var rendered = stdout.ToString();
		rendered.Should().Contain("# Ineligible");
		rendered.Should().Contain("GCSE English Language below the pass grade (4)");
		rendered.Should().Contain("Fewer than the required number of GCSE passes (5 at grade 4 or above)");
		rendered.Should().NotContain("downgraded:");
		rendered.Should().NotContain("green** because:");
	}

	[Fact]
	public void renderer_consumes_a_prebuilt_explained_result_without_second_evaluation()
	{
		var explained = new ExplainedResult(
			true,
			[],
			[
				new(
					Subject.Maths,
					Rating.Green,
					"final reason",
					Rating.Green,
					"subject-ratings",
					"base reason",
					ALevelGrade.B + 0.2,
					[]) {
					EntryEquivalentReason = "Entry equivalent satisfied by prior qualification applied_science btec_diploma distinction for biology.",
				},
			],
			new(1, 0, 50.0));

		using var stdout = new StringWriter();

		ExplanationRenderer.Render(explained, stdout);

		var rendered = stdout.ToString();
		rendered.Should().Contain("# Eligible");
		rendered.Should().Contain("final reason");
		rendered.Should().Contain("The engine rated this **green** because: base reason (predicted B, ~4.2).");
		rendered.Should().Contain("Entry equivalent satisfied by prior qualification");
		rendered.Should().Contain("applied\\_science").And.Contain("btec\\_diploma").And.Contain("distinction");
		rendered.Should().Contain("This was not downgraded.");
		rendered.Should().Contain("1 green, 0 amber; projected UCAS tariff 50");
	}
}
