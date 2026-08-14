namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 3.1 — a smoke test that the gate-clearing search (§1.6) reaches
///     Elite's five simultaneous eligibility rules through the real Elite engine within its widened
///     advice_max_grade_cost/advice_max_subjects_changed budget.
/// </summary>
public sealed class EliteAdviceTests
{
	[Fact]
	public void gate_clearing_advice_reaches_all_five_elite_eligibility_rules_from_a_near_miss_student()
	{
		var engine = EliteHarness.ShippedEngine();
		// Eight GCSEs already satisfying count/best-eight-total/top-seven-average (all at 8, total 64,
		// average 8.0); only English and Maths (both at 6) are short of the grade-7 gate. This keeps the
		// search small — two subjects, two grade steps — while still exercising the real Elite pipeline's
		// five simultaneous eligibility rules through the generalised gate-clearing search.
		var student = new StudentInput("S-ELITE-CLOSE", new Dictionary<string, int> {
			["english_language"] = 6,
			["maths"] = 6,
			["biology"] = 8,
			["chemistry"] = 8,
			["history"] = 8,
			["physics"] = 8,
			["psychology"] = 8,
			["french"] = 8,
		}, []) {
			DateOfBirth = new(2009, 9, 1),
		};

		engine.Explain(student).Eligible.Should().BeFalse();

		var advice = engine.Advise(student);

		advice.Eligible.Should().BeFalse();
		advice.Gate.Should().NotBeNull();
		advice.Gate!.Reachable.Should().BeTrue();

		var improved = ApplyChanges(student, advice.Gate.Changes);
		engine.Explain(improved).Eligible.Should().BeTrue();
	}

	private static StudentInput ApplyChanges(StudentInput student, IEnumerable<GradeChange> changes)
	{
		var gcses = student.Gcses?.ToDictionary(static kv => kv.Key, static kv => kv.Value) ?? [];
		foreach (var change in changes) {
			gcses[change.GcseSubject] = change.To;
		}

		return student with {
			Gcses = EquatableDictionaryFactory.CopyOf(gcses),
		};
	}
}
