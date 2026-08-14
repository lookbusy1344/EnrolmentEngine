namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 1.2 — the reusable top-N GCSE aggregate surface
///     (<see cref="GcseFacts.Count" />, <see cref="GcseFacts.BestTotal" />, <see cref="GcseFacts.BestAverage" />)
///     that a top-N eligibility rule (best-eight total, top-seven average) will bind to. Exercised
///     directly against <see cref="GcseFacts" /> rather than through a workflow, since the workflow
///     wiring is Phase 1.3.
/// </summary>
public sealed class GcseFactsTests
{
	private static GcseFacts Facts(params (string Subject, int Grade)[] gcses) =>
		new(gcses.Select(static g => new GcseResult(g.Subject, g.Grade)));

	[Fact]
	public void count_reports_the_number_of_distinct_submitted_subjects()
	{
		var facts = Facts(("maths", 9), ("english_language", 7), ("physics", 6));

		facts.Count.Should().Be(3);
	}

	[Fact]
	public void count_is_zero_for_no_gcses()
	{
		var facts = Facts();

		facts.Count.Should().Be(0);
	}

	[Fact]
	public void best_total_sums_exactly_the_requested_number_of_highest_grades()
	{
		var facts = Facts(("a", 9), ("b", 8), ("c", 7), ("d", 6), ("e", 5));

		facts.BestTotal(3).Should().Be(9 + 8 + 7);
	}

	[Fact]
	public void best_average_uses_the_same_top_n_projection_not_the_full_average()
	{
		var facts = Facts(("a", 9), ("b", 9), ("c", 1), ("d", 1));

		facts.BestAverage(2).Should().Be(9.0);
	}

	[Fact]
	public void more_than_n_gcses_ignores_the_lower_grades()
	{
		var facts = Facts(("a", 9), ("b", 9), ("c", 9), ("d", 9), ("e", 9), ("f", 9), ("g", 9), ("h", 9), ("i", 1), ("j", 1));

		facts.BestTotal(8).Should().Be(72);
		facts.BestAverage(7).Should().Be(9.0);
	}

	[Fact]
	public void fewer_than_n_gcses_sums_only_what_was_submitted()
	{
		var facts = Facts(("a", 9), ("b", 8));

		facts.BestTotal(8).Should().Be(17);
		facts.Count.Should().BeLessThan(8);
	}

	[Fact]
	public void empty_input_best_total_and_average_are_zero()
	{
		var facts = Facts();

		facts.BestTotal(8).Should().Be(0);
		facts.BestAverage(8).Should().Be(0.0);
	}

	[Fact]
	public void ties_are_deterministic_and_do_not_change_the_numeric_result()
	{
		var facts = Facts(("a", 7), ("b", 7), ("c", 7), ("d", 7));

		facts.BestTotal(2).Should().Be(14);
	}

	[Fact]
	public void repeated_low_level_subject_keys_retain_the_existing_best_grade_behaviour()
	{
		var facts = Facts(("maths", 5), ("MATHS", 9), ("Maths", 4));

		facts.Count.Should().Be(1);
		facts.BestTotal(1).Should().Be(9);
		facts.Grade("maths").Should().Be(9);
	}
}
