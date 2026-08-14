namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 1.1 — a baseline pinned against the <em>current</em> shipped
///     Standard policy (<c>workflows/</c> + <c>data/</c>) before any reusable-machinery generalisation
///     (top-N GCSE facts, eligibility rule projection, catalogue decoupling, final-programme validation,
///     workflow-driven advice) begins. Every later Phase 1 step must keep these exact results.
///     Deliberately independent of <see cref="GoldenFileTests" />: it drives the <c>Validated</c> entry
///     points (<see cref="EnrolmentEngine.EvaluateValidated" />, <see cref="EnrolmentEngine.ExplainValidated" />,
///     <see cref="EnrolmentEngine.AdviseValidated" />) that the multi-policy comparison work in later
///     phases builds on, not just the unchecked <c>Evaluate</c>/<c>Explain</c> paths the goldens cover.
/// </summary>
public sealed class PolicyIsolationTests
{
	private static string GoldenDir => Path.Combine(Harness.RepoRoot, "examples", "golden");

	private static StudentInput LoadFixture(string fixture)
	{
		using var stream = File.OpenRead(Path.Combine(GoldenDir, fixture + ".json"));
		return JsonSerializer.Deserialize(stream, EnrolmentJsonContext.Default.StudentDocument)!.Student;
	}

	[Fact]
	public void standard_eligibility_gate_pins_its_reason_order_and_wording()
	{
		var engine = Harness.ShippedEngine();
		var student = LoadFixture("ineligible-no-english");

		var validated = engine.EvaluateValidated(student);

		validated.Validation.IsValid.Should().BeTrue();
		validated.Value.Should().NotBeNull();
		validated.Value!.Eligible.Should().BeFalse();
		validated.Value.EligibilityReasons.Should().Equal(
			$"GCSE English Language below the pass grade ({Harness.Thresholds.PassGrade})",
			$"Fewer than the required number of GCSE passes ({Harness.Thresholds.MinPasses} at grade {Harness.Thresholds.PassGrade} or above)");
	}

	[Fact]
	public void standard_subject_rating_pins_green_amber_red_for_representative_subjects()
	{
		var engine = Harness.ShippedEngine();
		var allEights = LoadFixture("strong-constraints");
		var allrounder = LoadFixture("top-allrounder");

		var greenAndAmber = engine.EvaluateValidated(allEights);
		var red = engine.EvaluateValidated(allrounder);

		greenAndAmber.Validation.IsValid.Should().BeTrue();
		greenAndAmber.Value!.Recommendations.Single(r => r.Subject == Subject.Art).Rating.Should().Be(Rating.Green);
		greenAndAmber.Value.Recommendations.Single(r => r.Subject == Subject.Music).Rating.Should().Be(Rating.Amber);

		red.Validation.IsValid.Should().BeTrue();
		red.Value!.Recommendations.Single(r => r.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Red);
	}

	[Fact]
	public void standard_explanation_pins_baseline_narration_through_the_validated_path()
	{
		var engine = Harness.ShippedEngine();
		var student = LoadFixture("top-allrounder");

		var validated = engine.ExplainValidated(student);

		validated.Validation.IsValid.Should().BeTrue();
		var art = validated.Value!.Explanations.Single(e => e.Subject == Subject.Art);
		art.Rating.Should().Be(Rating.Green);
		art.BaseRating.Should().Be(Rating.Green);

		var furtherMaths = validated.Value.Explanations.Single(e => e.Subject == Subject.FurtherMaths);
		furtherMaths.Rating.Should().Be(Rating.Red);
	}

	[Fact]
	public void standard_advice_pins_the_committed_counterfactual_golden_through_the_validated_path()
	{
		var engine = Harness.ShippedEngine();
		var student = LoadFixture("advise-counterfactual");
		var expected = File.ReadAllText(Path.Combine(GoldenDir, "advise-counterfactual.expected.json"));

		var validated = engine.AdviseValidated(student);

		validated.Validation.IsValid.Should().BeTrue();
		var actual = JsonSerializer.Serialize(validated.Value, EnrolmentJsonContext.Default.AdviceResult);
		actual.ReplaceLineEndings().TrimEnd().Should().Be(expected.ReplaceLineEndings().TrimEnd());
	}

	[Fact]
	public void standard_choice_cap_is_off_by_default_and_clamps_greens_only_when_configured()
	{
		var rules = Harness.BuildFromShippedWorkflows().Engine;
		var uncapped = new EnrolmentEngine(rules, Harness.Thresholds, Harness.Catalogue, Harness.AsOf, Harness.Scale, Harness.BuildFromShippedWorkflows().Workflows);
		const int cap = 4;
		var capped = new EnrolmentEngine(rules, Harness.Thresholds with {
			MaxGreenChoices = cap,
		}, Harness.Catalogue, Harness.AsOf, Harness.Scale, Harness.BuildFromShippedWorkflows().Workflows);
		var student = LoadFixture("strong-constraints");

		Harness.Thresholds.MaxGreenChoices.Should().BeNull();
		var uncappedResult = uncapped.EvaluateValidated(student);
		uncappedResult.Validation.IsValid.Should().BeTrue();
		uncappedResult.Value!.Adjustments.Should().NotContain(a => a.Reason == Aggregator.ExceedsCapReason);

		var cappedResult = capped.EvaluateValidated(student);
		cappedResult.Validation.IsValid.Should().BeTrue();
		cappedResult.Value!.Summary.GreenCount.Should().Be(cap);
		cappedResult.Value.Adjustments.Should().Contain(a => a.Reason.StartsWith(Aggregator.ExceedsCapReason, StringComparison.Ordinal));
	}
}
