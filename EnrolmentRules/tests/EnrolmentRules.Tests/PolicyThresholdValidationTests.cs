namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 1.2 — the optional top-N GCSE eligibility knobs
///     (<c>best_gcse_count</c>, <c>min_best_gcse_points</c>, <c>top_gcse_average_count</c>,
///     <c>min_top_gcse_average</c>) and <c>min_chosen_a_levels</c> on <see cref="PolicyThresholds" />.
///     The four top-N knobs are optional for Standard and must validate all-or-none; the shipped
///     <c>data/thresholds.yaml</c> is deliberately left unchanged (none of them present).
/// </summary>
public sealed class PolicyThresholdValidationTests
{
	private const string RequiredFields = """
										  pass_grade: 4
										  min_passes: 5
										  top_entry: 7
										  standard_entry: 5
										  exceptional_entry: 8
										  min_dfe_green_probability_at_or_above: 0.60
										  min_dfe_amber_probability_at_or_above: 0.50
										  max_chosen_a_levels: 3
										  high_attainment_max_chosen_a_levels: 4
										  high_attainment_average_gcse: 7.5
										  amber_score_factor: 0.5
										  """;

	private static PolicyThresholds Load(string extra)
	{
		var schema = File.ReadAllText(Path.Combine(Harness.DataDir, PolicyThresholdsStore.SchemaFileName));
		return PolicyThresholdsStore.LoadAndValidate(
			new StringReader(RequiredFields + Environment.NewLine + extra),
			new StringReader(schema),
			"test-thresholds.yaml");
	}

	[Fact]
	public void shipped_thresholds_leave_the_top_n_knobs_unset()
	{
		Harness.Thresholds.BestGcseCount.Should().BeNull();
		Harness.Thresholds.MinBestGcsePoints.Should().BeNull();
		Harness.Thresholds.TopGcseAverageCount.Should().BeNull();
		Harness.Thresholds.MinTopGcseAverage.Should().BeNull();
		Harness.Thresholds.MinChosenALevels.Should().Be(0);
	}

	[Fact]
	public void all_four_top_n_knobs_together_load_and_validate()
	{
		var thresholds = Load("""
							  best_gcse_count: 8
							  min_best_gcse_points: 60
							  top_gcse_average_count: 7
							  min_top_gcse_average: 7.0
							  """);

		thresholds.BestGcseCount.Should().Be(8);
		thresholds.MinBestGcsePoints.Should().Be(60);
		thresholds.TopGcseAverageCount.Should().Be(7);
		thresholds.MinTopGcseAverage.Should().Be(7.0);
	}

	[Theory]
	[InlineData("min_best_gcse_points: 60\ntop_gcse_average_count: 7\nmin_top_gcse_average: 7.0")]
	[InlineData("best_gcse_count: 8\ntop_gcse_average_count: 7\nmin_top_gcse_average: 7.0")]
	[InlineData("best_gcse_count: 8\nmin_best_gcse_points: 60\nmin_top_gcse_average: 7.0")]
	[InlineData("best_gcse_count: 8\nmin_best_gcse_points: 60\ntop_gcse_average_count: 7")]
	public void a_partial_set_of_top_n_knobs_fails_load(string extra)
	{
		var act = () => Load(extra);

		act.Should().Throw<PolicyThresholdsException>().WithMessage("*all four*");
	}

	[Fact]
	public void top_gcse_average_count_exceeding_best_gcse_count_fails_load()
	{
		var act = () => Load("""
							 best_gcse_count: 6
							 min_best_gcse_points: 40
							 top_gcse_average_count: 7
							 min_top_gcse_average: 7.0
							 """);

		act.Should().Throw<PolicyThresholdsException>().WithMessage("*top_gcse_average_count*best_gcse_count*");
	}

	[Fact]
	public void min_best_gcse_points_above_the_reachable_maximum_fails_load()
	{
		var act = () => Load("""
							 best_gcse_count: 8
							 min_best_gcse_points: 73
							 top_gcse_average_count: 7
							 min_top_gcse_average: 7.0
							 """);

		act.Should().Throw<PolicyThresholdsException>().WithMessage("*min_best_gcse_points*reachable*");
	}

	[Fact]
	public void min_chosen_a_levels_defaults_to_zero_when_absent()
	{
		var thresholds = Load(string.Empty);

		thresholds.MinChosenALevels.Should().Be(0);
	}

	[Fact]
	public void min_chosen_a_levels_beyond_the_high_attainment_maximum_fails_load()
	{
		var act = () => Load("min_chosen_a_levels: 5");

		act.Should().Throw<PolicyThresholdsException>().WithMessage("*min_chosen_a_levels*high_attainment_max_chosen_a_levels*");
	}

	[Fact]
	public void min_chosen_a_levels_at_the_high_attainment_maximum_loads()
	{
		var thresholds = Load("min_chosen_a_levels: 4");

		thresholds.MinChosenALevels.Should().Be(4);
	}
}
