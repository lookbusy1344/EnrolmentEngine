namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 3.1 — the eligibility boundary matrix, driven through the real
///     Elite engine (<see cref="EliteHarness" />), never by evaluating helper calculations in isolation.
///     Note on the 60-point/7.0-average pair: given a max GCSE grade of 9, a top-seven average of exactly
///     7.0 caps the best-eight total at 56 (7 × 7 plus an eighth grade that cannot exceed the top-seven's
///     own minimum) — the same tension the plan's risk table records ("straight grade 7s fail... best-eight
///     total is 56"). The average-boundary test below therefore checks the average rule's own reason
///     independently of overall eligibility, since the total rule is unavoidably failing at that exact point.
/// </summary>
public sealed class EliteEligibilityTests
{

	private const string BestEightTotalReason = "Best eight GCSE total";
	private const string TopSevenAverageReason = "Top seven GCSE average";
	private static StudentInput Student(Dictionary<string, int> gcses) =>
		new("S-ELITE", gcses, []) {
			DateOfBirth = new(2009, 9, 1),
		};

	[Fact]
	public void the_elite_engine_builds_and_probe_compiles()
	{
		var act = EliteHarness.ShippedEngine;

		act.Should().NotThrow();
	}

	[Fact]
	public void seven_grade_nine_gcses_fail_the_at_least_eight_requirement_despite_a_high_total()
	{
		var engine = EliteHarness.ShippedEngine();
		var student = Student(new() {
			["english_language"] = 9,
			["maths"] = 9,
			["biology"] = 9,
			["chemistry"] = 9,
			["history"] = 9,
			["physics"] = 9,
			["psychology"] = 9,
		});

		var result = engine.Explain(student);

		result.Eligible.Should().BeFalse();
		result.EligibilityReasons.Should().Contain(r => r.Contains("eight GCSE results", StringComparison.Ordinal));
	}

	[Fact]
	public void eight_grade_sevens_fail_with_a_best_eight_total_of_56()
	{
		var engine = EliteHarness.ShippedEngine();
		var student = Student(EliteHarness.EightGcsesAtGrade(7));

		var result = engine.Explain(student);

		result.Eligible.Should().BeFalse();
		result.EligibilityReasons.Should().Contain(r => r.Contains(BestEightTotalReason, StringComparison.Ordinal));
		// The reason spells out the gap: the 60-point minimum against the student's actual 56.
		result.EligibilityReasons.Should().Contain(
			r => r.Contains("needs 60", StringComparison.Ordinal) && r.Contains("actual 56", StringComparison.Ordinal));
	}

	[Fact]
	public void a_best_eight_total_of_59_fails_and_60_passes()
	{
		var engine = EliteHarness.ShippedEngine();

		// Seven subjects at grade 8 (56) plus an eighth at 3 = 59, or at 4 = 60. Top-seven average is 8.0
		// in both cases, well clear of the 7.0 bar, so only the total rule's boundary is exercised.
		var fiftyNine = EliteHarness.EightGcsesAtGrade(8);
		fiftyNine["french"] = 3;
		var fiftyNineResult = engine.Explain(Student(fiftyNine));

		var sixty = EliteHarness.EightGcsesAtGrade(8);
		sixty["french"] = 4;
		var sixtyResult = engine.Explain(Student(sixty));

		fiftyNineResult.EligibilityReasons.Should().Contain(r => r.Contains(BestEightTotalReason, StringComparison.Ordinal));
		sixtyResult.EligibilityReasons.Should().NotContain(r => r.Contains(BestEightTotalReason, StringComparison.Ordinal));
		sixtyResult.Eligible.Should().BeTrue();
	}

	[Fact]
	public void adding_low_extra_gcses_does_not_lower_the_best_eight_total_or_top_seven_average()
	{
		var engine = EliteHarness.ShippedEngine();
		var eightAtEight = EliteHarness.EightGcsesAtGrade(8);
		var withExtras = new Dictionary<string, int>(eightAtEight, StringComparer.Ordinal) {
			["german"] = 1,
			["art"] = 1,
		};

		var withoutExtras = engine.Explain(Student(eightAtEight));
		var withExtrasResult = engine.Explain(Student(withExtras));

		withoutExtras.Eligible.Should().BeTrue();
		withExtrasResult.Eligible.Should().BeTrue();
	}

	[Fact]
	public void top_seven_average_immediately_below_seven_fails_and_exactly_seven_passes()
	{
		var engine = EliteHarness.ShippedEngine();

		// Top-seven sum 48 (avg 6.857) vs 49 (avg exactly 7.0); the eighth grade stays low so it is
		// never selected into the top seven. The overall total (<= 56) unavoidably still fails at this
		// exact average boundary (see the class doc comment), so only the average rule's own reason is
		// checked, not overall eligibility.
		var belowAverage = Student(new() {
			["english_language"] = 7,
			["maths"] = 7,
			["biology"] = 7,
			["chemistry"] = 7,
			["history"] = 7,
			["physics"] = 7,
			["psychology"] = 6,
			["french"] = 1,
		});
		var atAverage = Student(new() {
			["english_language"] = 7,
			["maths"] = 7,
			["biology"] = 7,
			["chemistry"] = 7,
			["history"] = 7,
			["physics"] = 7,
			["psychology"] = 7,
			["french"] = 1,
		});

		var belowResult = engine.Explain(belowAverage);
		var atResult = engine.Explain(atAverage);

		belowResult.EligibilityReasons.Should().Contain(r => r.Contains(TopSevenAverageReason, StringComparison.Ordinal));
		// The reason spells out the gap: the 7.0 average minimum against the student's actual 6.9 (48 ÷ 7).
		belowResult.EligibilityReasons.Should().Contain(
			r => r.Contains("needs 7.0", StringComparison.Ordinal) && r.Contains("actual 6.9", StringComparison.Ordinal));
		atResult.EligibilityReasons.Should().NotContain(r => r.Contains(TopSevenAverageReason, StringComparison.Ordinal));
	}

	[Fact]
	public void english_language_six_fails_and_seven_passes_the_boundary()
	{
		var engine = EliteHarness.ShippedEngine();
		var gcses = EliteHarness.EightGcsesAtGrade(8);

		gcses["english_language"] = 6;
		var belowResult = engine.Explain(Student(gcses));

		gcses["english_language"] = 7;
		var atResult = engine.Explain(Student(gcses));

		belowResult.Eligible.Should().BeFalse();
		belowResult.EligibilityReasons.Should().Contain(r => r.Contains("English", StringComparison.Ordinal));
		atResult.Eligible.Should().BeTrue();
	}

	[Fact]
	public void maths_six_fails_and_seven_passes_the_boundary()
	{
		var engine = EliteHarness.ShippedEngine();
		var gcses = EliteHarness.EightGcsesAtGrade(8);

		gcses["maths"] = 6;
		var belowResult = engine.Explain(Student(gcses));

		gcses["maths"] = 7;
		var atResult = engine.Explain(Student(gcses));

		belowResult.Eligible.Should().BeFalse();
		belowResult.EligibilityReasons.Should().Contain(r => r.Contains("Maths", StringComparison.Ordinal));
		atResult.Eligible.Should().BeTrue();
	}

	[Fact]
	public void multiple_failures_preserve_declared_reason_order()
	{
		var engine = EliteHarness.ShippedEngine();
		var student = Student(new() {
			["english_language"] = 4,
			["maths"] = 4,
		});

		var result = engine.Explain(student);

		result.Eligible.Should().BeFalse();
		result.EligibilityReasons.Should().HaveCountGreaterThanOrEqualTo(3);
		result.EligibilityReasons[0].Should().Contain("English");
		result.EligibilityReasons[1].Should().Contain("Maths");
	}

	[Fact]
	public void criteria_state_each_loaded_threshold_accurately()
	{
		var engine = EliteHarness.ShippedEngine();

		var criteria = engine.Describe(Subject.Biology);

		criteria.Eligibility.Should().Contain(c => c.Contains('7') && c.Contains("English", StringComparison.Ordinal));
		criteria.Eligibility.Should().Contain(c => c.Contains('8'));
		criteria.Eligibility.Should().Contain(c => c.Contains("60", StringComparison.Ordinal));
		criteria.Eligibility.Should().Contain(c => c.Contains('7') && c.Contains("average", StringComparison.OrdinalIgnoreCase));
	}
}
