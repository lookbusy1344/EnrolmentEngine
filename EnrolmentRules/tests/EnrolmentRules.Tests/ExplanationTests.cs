namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Cli;
using Domain;
using Engine;

/// <summary>
///     Phase 7 — explanations (provenance end-to-end). Each <see cref="Explanation" /> is "the winning
///     rule's reason, plus any adjustment that overrode it": the deciding engine rule (name +
///     <c>SuccessEvent</c>), the predicted points the tier matched on, and the host-code
///     <see cref="Adjustment" /> trail. Driven through the real engine + constraint pass, never by reading
///     JSON. The golden-file end-to-end suite lives in <see cref="GoldenFileTests" />.
/// </summary>
public sealed class ExplanationTests
{
	private static CatalogueData ShippedCatalogue => CatalogueStore.LoadAndValidate(Harness.DataDir);

	// A uniformly strong student (avg 8.0): clears the gate and predicts well above the green tiers, so
	// the explanation has a real winning rule to cite.
	private static StudentInput StrongStudent(params string[] hobbies) =>
		new("S-EXPLAIN", new Dictionary<string, int> {
			["english_language"] = 8,
			["maths"] = 8,
			["physics"] = 8,
			["chemistry"] = 8,
			["biology"] = 8,
			["english_literature"] = 8,
			["french"] = 8,
			["german"] = 8,
			["physical_education"] = 8,
			["computer_studies"] = 8,
			["history"] = 8,
			["music"] = 8,
			["art"] = 8,
		}, hobbies);

	private static StudentInput StrongBiologyEquivalentStudent() =>
		new("S-ENTRY-EXPLAIN", new Dictionary<string, int> {
			["english_language"] = 8,
			["maths"] = 8,
			["physics"] = 8,
			["chemistry"] = 8,
			["biology"] = 1,
			["english_literature"] = 8,
			["french"] = 8,
			["german"] = 8,
			["physical_education"] = 8,
			["computer_studies"] = 8,
			["history"] = 8,
			["music"] = 8,
			["art"] = 8,
		}, []) { PriorQualifications = [new("applied_science", QualificationType.BtecDiploma, "distinction")] };

	private static double PredictedPoints(Subject subject, double average) => ShippedCatalogue.Meta(subject).Regression.Predict(average);

	private static Explanation Of(ExplainedResult result, Subject subject) =>
		result.Explanations.Single(e => e.Subject == subject);

	[Fact]
	public async Task explanation_names_the_winning_rule_and_cites_the_predicted_grade()
	{
		var engine = await Harness.ShippedEngineAsync();
		var explained = await engine.ExplainAsync(StrongStudent("plays_piano"));

		var physics = Of(explained, Subject.Physics);

		// Physics ends green, decided by the physics:green rule with no host-code override (the green cap is
		// disabled by default), citing the predicted points the tier matched.
		physics.Rating.Should().Be(Rating.Green);
		physics.BaseRating.Should().Be(Rating.Green);
		physics.Rule.Should().Be(RatingEvaluator.RuleName(Subject.Physics, Rating.Green));
		physics.BaseReason.Should().Be(physics.Reason);
		physics.BaseReason.Should().Contain("green threshold");
		physics.PredictedPoints.Should().Be(PredictedPoints(Subject.Physics, 8.0));
		physics.Overrides.Should().BeEmpty();
	}

	[Fact]
	public async Task explanation_cites_the_constraint_that_overrode_the_base_rule()
	{
		var engine = await Harness.ShippedEngineAsync();
		var explained = await engine.ExplainAsync(StrongStudent("plays_piano"));

		// History ↔ Art clash: both green at base, Art is the lower-weight loser → amber. The explanation
		// must cite the exclusion adjustment, not just the base art:green rule.
		var art = Of(explained, Subject.Art);

		art.BaseRating.Should().Be(Rating.Green);
		art.Rule.Should().Be(RatingEvaluator.RuleName(Subject.Art, Rating.Green));
		art.Rating.Should().Be(Rating.Amber);

		var exclusion = art.Overrides.Should().ContainSingle().Which;
		exclusion.From.Should().Be(Rating.Green);
		exclusion.To.Should().Be(Rating.Amber);
		exclusion.Reason.Should().Contain("Mutual exclusion").And.Contain(EnumNames.NameOf(Subject.History));
		art.Reason.Should().Be(exclusion.Reason);
	}

	[Fact]
	public async Task explanation_mentions_the_prior_qualification_that_opened_entry()
	{
		var engine = await Harness.ShippedEngineAsync();
		var explained = await engine.ExplainAsync(StrongBiologyEquivalentStudent());

		var biology = Of(explained, Subject.Biology);

		biology.Rating.Should().BeOneOf(Rating.Green, Rating.Amber);
		biology.EntryEquivalentReason.Should().NotBeNull();
		biology.EntryEquivalentReason.Should().Contain("applied_science").And.Contain("btec_diploma").And.Contain("distinction");
	}

	[Fact]
	public async Task ineligible_explanation_attributes_each_red_to_the_eligibility_gate()
	{
		// Only Maths present: English absent and too few passes ⇒ ineligible, every subject red.
		var student = new StudentInput("S-INELIGIBLE", new Dictionary<string, int> { ["maths"] = 6 }, []);

		var engine = await Harness.ShippedEngineAsync();
		var explained = await engine.ExplainAsync(student);

		explained.Eligible.Should().BeFalse();
		explained.Explanations.Should().OnlyContain(e => e.Rating == Rating.Red);
		explained.Explanations.Should().OnlyContain(e => e.Rule == RatingEvaluator.EligibilityWorkflow);
		explained.Explanations.Should().OnlyContain(e => e.BaseReason.Contains("English", StringComparison.Ordinal));
	}

	[Fact]
	public async Task the_plain_and_explained_results_agree_on_ratings_and_summary()
	{
		var student = StrongStudent("plays_piano");
		var engine = await Harness.ShippedEngineAsync();

		var result = await engine.EvaluateAsync(student);
		var explained = await engine.ExplainAsync(student);

		// The two views are projections of one evaluation: same eligibility, same summary, and the same
		// final rating per subject in the same ranked order.
		explained.Eligible.Should().Be(result.Eligible);
		explained.Summary.Should().Be(result.Summary);
		explained.Explanations.Select(e => (e.Subject, e.Rating))
			.Should().Equal(result.Recommendations.Select(r => (r.Subject, r.Rating)));
	}

	[Fact]
	public async Task cli_explain_emits_a_parseable_explained_result()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "student.json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--explain", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();

		var explained = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.ExplainedResult);
		explained.Should().NotBeNull();
		explained!.Explanations.Select(e => e.Subject).Should().BeEquivalentTo(Catalogue.Subjects);
		explained.Explanations.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Rule));
	}

	[Fact]
	public async Task cli_json_emits_a_parseable_enrolment_result()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "student.json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		var result = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.EnrolmentResult);
		result.Should().NotBeNull();
		result!.Recommendations.Should().HaveCount(Catalogue.Subjects.Count);
	}
}
