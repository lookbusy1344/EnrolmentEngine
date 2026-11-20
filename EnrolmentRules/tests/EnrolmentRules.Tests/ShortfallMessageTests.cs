namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using FsCheck.Xunit;
using Prediction;

/// <summary>
///     <see cref="Explanation.Message" />: one short line telling the student what stands between them and the
///     next rating up. Driven through the real engine. Expected values are hand-calculated from
///     <c>data/catalogue.yaml</c> regressions, <c>data/thresholds.yaml</c> and the DfE transition matrix.
/// </summary>
public sealed class ShortfallMessageTests
{
	private readonly EnrolmentEngine engine = Harness.ShippedEngine();

	// Seven GCSEs at grade 4 — enough passes to clear the gate (English, Maths, five at grade 4).
	private static Dictionary<string, int> SevenFours() => new(StringComparer.Ordinal) {
		["maths"] = 4,
		["english_language"] = 4,
		["english_literature"] = 4,
		["physics"] = 4,
		["chemistry"] = 4,
		["biology"] = 4,
		["history"] = 4,
	};

	private static StudentInput Student(Dictionary<string, int> gcses, params string[] hobbies) => new("S-SHORTFALL", gcses, hobbies);

	private Explanation Explain(StudentInput student, Subject subject) =>
		engine.Explain(student).Explanations.Single(e => e.Subject == subject);

	[Fact]
	public void amber_held_back_only_by_the_national_figure_names_it_and_the_average_that_clears_it()
	{
		// Average 33/8 = 4.125, DfE band "4 to < 5". Computer Studies green needs P(>= D) >= 60%:
		// 1 - 0.14419 (U) - 0.26342 (E) = 59.2%, shown floored as 59%. Band "5 to < 6" gives
		// 1 - 0.04106 - 0.15180 = 80.7%, and every average 4.2..4.9 stays in the failing band, so 5.0 is the
		// lowest average that clears it. Prediction max(0.9 * 4.125 - 2.1, 0.9 * 5 - 2.1) = 2.40 >= D already passes.
		var gcses = SevenFours();
		gcses["computer_studies"] = 5;

		var explanation = Explain(Student(gcses), Subject.ComputerStudies);

		explanation.Rating.Should().Be(Rating.Amber);
		// The message opens with the computer_studies:amber SuccessEvent: why the subject is amber, not red.
		explanation.Message.Should().Be(
			"Entry met (Computer Studies at standard entry); predicted A-level grade at or above the amber threshold. "
			+ "Green needs at least 60% of similar students nationally to reach grade D or better (yours: 59%). "
			+ "A GCSE average of 5.0 would get green (yours: 4.1).");
	}

	[Fact]
	public void red_held_back_by_a_gcse_grade_names_the_grade_and_gives_no_average()
	{
		// Average 4.0. Amber needs Computer Studies GCSE >= 5 (standard_entry). Prediction max(0.9 * 4 - 2.1,
		// own 4 -> 1.5) = 1.5 >= E passes; DfE P(>= E) in band "4 to < 5" = 1 - 0.14419 = 85.6% >= 50% passes.
		// Raising the average cannot change the GCSE grade, so no average is offered.
		var gcses = SevenFours();
		gcses["computer_studies"] = 4;

		var explanation = Explain(Student(gcses), Subject.ComputerStudies);

		explanation.Rating.Should().Be(Rating.Red);
		explanation.Message.Should().Be("Amber needs GCSE Computer Studies at grade 5 or above (yours: 4).");
	}

	[Fact]
	public void a_gcse_the_student_has_not_taken_says_so()
	{
		var explanation = Explain(Student(SevenFours()), Subject.ComputerStudies);

		explanation.Rating.Should().Be(Rating.Red);
		explanation.Message.Should().Be("Amber needs GCSE Computer Studies at grade 5 or above (not taken).");
	}

	[Fact]
	public void every_failed_requirement_is_listed_in_rule_order()
	{
		// Average (5 * 4 + 1 + 1 + 1) / 8 = 2.875, DfE band "2 to < 3". Amber needs: Computer Studies >= 5 (has 1);
		// prediction max(0.9 * 2.875 - 2.1, 0.9 * 1 - 2.1) = 0.49 < E, shown floored as U; DfE P(>= E) =
		// 1 - 0.53333 = 46.7% < 50%, shown floored as 46%.
		var gcses = new Dictionary<string, int>(StringComparer.Ordinal) {
			["maths"] = 4,
			["english_language"] = 4,
			["english_literature"] = 4,
			["physics"] = 4,
			["chemistry"] = 4,
			["biology"] = 1,
			["history"] = 1,
			["computer_studies"] = 1,
		};

		var explanation = Explain(Student(gcses), Subject.ComputerStudies);

		explanation.Message.Should().Be(
			"Amber needs GCSE Computer Studies at grade 5 or above (yours: 1); a predicted grade E or better (yours: U); "
			+ "at least 50% of similar students nationally to reach grade E or better (yours: 46%).");
	}

	[Fact]
	public void an_average_requirement_is_not_repeated_as_an_average_hint()
	{
		// History amber needs average >= 5.0 (has 4.0). Prediction max(0.85 * 4 - 1.9, own 4 -> 1.5) = 1.5 >= E;
		// DfE P(>= E) = 1 - 0.02791 = 97.2% >= 50%. The failing clause is the average itself, so the line
		// already says what average is needed.
		var explanation = Explain(Student(SevenFours()), Subject.History);

		explanation.Rating.Should().Be(Rating.Red);
		explanation.Message.Should().Be("Amber needs a GCSE average of 5.0 or above (yours: 4.0).");
	}

	[Fact]
	public void alternatives_are_listed_as_either_or()
	{
		// Biology amber needs (Biology GCSE >= 5 || a prior qualification) — both fail with Biology at 4 and no
		// prior qualification. Prediction max(0.9 * 4 - 2.3, own 4 -> 1.3) = 1.3 >= E; DfE P(>= E) =
		// 1 - 0.22056 = 77.9% >= 50%.
		var explanation = Explain(Student(SevenFours()), Subject.Biology);

		explanation.Rating.Should().Be(Rating.Red);
		explanation.Message.Should().Be(
			"Amber needs either GCSE Biology at grade 5 or above (yours: 4), or a prior qualification that counts instead of GCSE Biology.");
	}

	[Fact]
	public void green_says_every_requirement_is_met()
	{
		var gcses = SevenFours().ToDictionary(static pair => pair.Key, static _ => 8, StringComparer.Ordinal);

		var explanation = Explain(Student(gcses), Subject.Physics);

		explanation.Rating.Should().Be(Rating.Green);
		explanation.Message.Should().Be("All requirements met.");
	}

	[Fact]
	public void a_downgrade_by_a_cross_subject_relationship_reports_that_relationship()
	{
		// Music requires an own-time "plays_" activity; without one the constraint pass downgrades green to amber.
		var gcses = SevenFours().ToDictionary(static pair => pair.Key, static _ => 8, StringComparer.Ordinal);
		gcses["music"] = 8;

		var explanation = Explain(Student(gcses), Subject.Music);

		explanation.BaseRating.Should().Be(Rating.Green);
		explanation.Rating.Should().Be(Rating.Amber);
		explanation.Message.Should().Be(explanation.Overrides.Single().Reason);
	}

	[Fact]
	public void an_ineligible_student_sees_the_gate_reason()
	{
		var gcses = SevenFours();
		gcses["maths"] = 3;

		var explanation = Explain(Student(gcses), Subject.ComputerStudies);

		explanation.Message.Should().Be("GCSE Maths below the pass grade (4)");
	}

	[Theory]
	[InlineData("workflows")]
	[InlineData("policies/elite/workflows")]
	public void every_shipped_rating_rule_is_interpretable(string workflowsDir)
	{
		var workflows = WorkflowStore.LoadAndValidate(Path.Combine(Harness.RepoRoot, workflowsDir), Harness.SchemaPath);

		var explainer = new ShortfallExplainer(workflows, Harness.Thresholds, Harness.Catalogue, Harness.Scale, DfeTransitionMatrix.LoadDefault());

		explainer.UninterpretableRules.Should().BeEmpty();
	}

	[Fact]
	public void a_rule_outside_the_interpreter_grammar_keeps_its_deciding_reason()
	{
		// Arithmetic is valid RulesEngine but outside the interpreter's grammar. Computer Studies rates red for
		// this student, and the amber rule it would be explained against cannot be read.
		var workflows = WorkflowStore.LoadAndValidate(Harness.WorkflowsDir);
		var amber = workflows.Single(w => w.WorkflowName == RatingEvaluator.SubjectRatingsWorkflow)
							 .Rules.Single(r => r.RuleName == "computer_studies:amber");
		amber.Expression += " && facts.Gcse(\"maths\") * 1 >= 0";
		var runtime = WorkflowStore.BuildEngine(workflows);
		var custom = new EnrolmentEngine(runtime, Harness.Thresholds, Harness.Catalogue, Harness.AsOf, Harness.Scale, workflows);

		var explanation = custom.Explain(Student(SevenFours())).Explanations.Single(e => e.Subject == Subject.ComputerStudies);

		explanation.Rating.Should().Be(Rating.Red);
		explanation.Message.Should().Be(explanation.Reason);
	}

	// Explain cross-checks every message against the engine's own verdict (the deciding tier must hold, the
	// tier above must fail on at least one clause) and throws on disagreement, so these properties are the
	// interpreter-versus-RulesEngine agreement check across both shipped policies.
	[Property(Arbitrary = new[] {
		typeof(InvariantTests.StudentArbitraries),
	}, MaxTest = 250)]
	public bool every_shipped_rule_explains_consistently_with_the_engine(StudentInput student) =>
		engine.Explain(student).Explanations.All(static e => e.Message.Length > 0);

	[Property(Arbitrary = new[] {
		typeof(InvariantTests.StudentArbitraries),
	}, MaxTest = 250)]
	public bool every_elite_rule_explains_consistently_with_the_engine(StudentInput student) =>
		EliteHarness.ShippedEngine().Explain(student).Explanations.All(static e => e.Message.Length > 0);
}
