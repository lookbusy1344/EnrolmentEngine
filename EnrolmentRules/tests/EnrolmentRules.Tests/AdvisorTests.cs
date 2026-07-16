namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Cli;
using Domain;

/// <summary>
///     Counterfactual advisor tests. These exercise the public advice surface through the real engine
///     and assert that any proposed grade changes actually re-rate the student when fed back through
///     the pipeline.
/// </summary>
public sealed class AdvisorTests
{
	private static StudentInput StrongStudent(params string[] hobbies) =>
		new("S-ADVSTRONG", new Dictionary<string, int> {
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

	[Fact]
	public void advisor_finds_the_smallest_bump_that_flips_a_red_subject()
	{
		var engine = Harness.ShippedEngine();

		var student = new StudentInput("S-ADVISE", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 7,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []);

		var advice = engine.Advise(student);
		// Maths is red on its hard Maths-8 entry gate; the smallest fix is a single-grade bump to the
		// exceptional grade.
		var maths = advice.Advice.Single(a => a.Subject == Subject.Maths);

		maths.Reachable.Should().BeTrue();
		maths.Target.Should().Be(Rating.Amber);
		maths.Changes.Should().ContainSingle(c => c.GcseSubject == "maths" && c.From == 7 && c.To == 8);

		var improved = ApplyChanges(student, maths.Changes);
		var explained = engine.Explain(improved);
		((int)explained.Explanations.Single(e => e.Subject == Subject.Maths).Rating)
			.Should().BeLessThanOrEqualTo((int)maths.Target);
	}

	[Fact]
	public void advisor_accepts_a_red_to_green_step_as_hitting_an_amber_target()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-RED-GREEN", new Dictionary<string, int> {
			["english_language"] = 9,
			["maths"] = 7,
			["physics"] = 9,
			["chemistry"] = 5,
			["biology"] = 9,
			["history"] = 9,
		}, []) { DateOfBirth = new DateOnly(2009, 9, 1) };

		var advice = engine.Advise(student);
		// Maths is red on its Maths-8 entry gate; its amber target is met by a single bump that actually
		// lands it green — a red→green step still satisfies the amber target.
		var maths = advice.Advice.Single(a => a.Subject == Subject.Maths);

		maths.Reachable.Should().BeTrue();
		maths.Target.Should().Be(Rating.Amber);
		maths.Changes.Should().BeEquivalentTo([new GradeChange("maths", 7, 8)]);

		var improved = ApplyChanges(student, maths.Changes);
		var explained = engine.Explain(improved);
		((int)explained.Explanations.Single(e => e.Subject == Subject.Maths).Rating).Should().BeLessThanOrEqualTo((int)maths.Target);
	}

	[Fact]
	public void advisor_preserves_date_of_birth_when_searching_age_gated_art_counterfactuals()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-ADULT-ART", new Dictionary<string, int> {
			["english_language"] = 9,
			["maths"] = 9,
			["chemistry"] = 9,
			["biology"] = 9,
			["history"] = 9,
			["art"] = Harness.Thresholds.StrongEntry,
		}, []) { DateOfBirth = Harness.AsOf.AddYears(-Harness.Thresholds.AdultAge) };

		var advice = engine.Advise(student);
		var art = advice.Advice.Single(a => a.Subject == Subject.Art);

		art.Reachable.Should().BeTrue();
		art.Changes.Should().NotBeEmpty();

		var improved = ApplyChanges(student, art.Changes);
		var explained = engine.Explain(improved);
		((int)explained.Explanations.Single(e => e.Subject == Subject.Art).Rating).Should().BeLessThanOrEqualTo((int)art.Target);
	}

	[Fact]
	public void advice_never_proposes_sitting_a_gcse_the_student_has_not_taken()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-HELD-ONLY", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []);

		var advice = engine.Advise(student);

		// The counterfactual search proposes raising GCSEs the student already holds, never sitting a brand
		// new one: a grade bump is actionable advice, "go and take another GCSE from scratch" is not.
		var held = student.Gcses!.Value;
		advice.Advice
			.SelectMany(static a => a.Changes)
			.Select(static c => c.GcseSubject)
			.Should().OnlyContain(subject => held.ContainsKey(subject));

		// Spanish A-level is gated on a French or German GCSE the student never sat, so with new GCSEs off
		// the table it is unreachable by grade changes alone rather than reached by inventing a qualification.
		advice.Advice.Single(a => a.Subject == Subject.Spanish).Reachable.Should().BeFalse();
	}

	[Fact]
	public void advisor_considers_unsat_gcses_when_asked_for_diagnosis()
	{
		var engine = Harness.ShippedEngine();
		// Maths is committed so Further Maths' prerequisite is met and its search returns early rather than
		// exhausting the (now 13-wide) diagnostic candidate space — keeps this slow-by-design path quick here.
		var student = new StudentInput("S-HELD-ONLY", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []) { ChosenALevels = [Subject.Maths] };

		var advice = engine.Advise(student, true);

		// Diagnostic mode reverts to the old, heavier behaviour: Spanish, gated on a French or German GCSE
		// the student never sat, becomes reachable by proposing those brand-new GCSEs.
		advice.Advice.Single(a => a.Subject == Subject.Spanish).Reachable.Should().BeTrue();
		advice.Advice
			.SelectMany(static a => a.Changes)
			.Select(static c => c.GcseSubject)
			.Should().Contain("french");
	}

	[Fact]
	public void advice_unsat_gcse_behaviour_defaults_from_thresholds()
	{
		var (_, rules) = Harness.BuildFromShippedWorkflows();
		var engine = new EnrolmentEngine(
			rules, Harness.Thresholds with { AdviceConsidersUnsatGcses = true }, Harness.Catalogue, Harness.AsOf);
		var student = new StudentInput("S-HELD-ONLY", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []) { ChosenALevels = [Subject.Maths] };

		// No per-call override: the diagnostic knob is read from the loaded thresholds, so Spanish is reachable.
		var advice = engine.Advise(student);

		advice.Advice.Single(a => a.Subject == Subject.Spanish).Reachable.Should().BeTrue();
	}

	[Fact]
	public void gate_clearing_advice_can_propose_brand_new_gcses_when_the_student_lacks_enough_passes()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-GATE",
			new Dictionary<string, int> { ["english_language"] = Harness.Thresholds.PassGrade, ["maths"] = Harness.Thresholds.PassGrade }, []) {
			DateOfBirth = new DateOnly(2009, 9, 1),
		};

		var advice = engine.Advise(student);

		advice.Eligible.Should().BeFalse();
		advice.Gate.Should().NotBeNull();
		advice.Gate!.Changes.Should().NotBeEmpty();
		advice.Gate.Changes.Select(change => change.GcseSubject)
			.Should().Contain(subject => subject != "english_language" && subject != "maths");
	}

	[Fact]
	public void advisor_reports_a_veto_blocked_subject_as_unreachable()
	{
		var engine = Harness.ShippedEngine();

		var advice = engine.Advise(StrongStudent("plays_trombone"));
		var music = advice.Advice.Single(a => a.Subject == Subject.Music);

		music.Reachable.Should().BeFalse();
		music.BlockedReason.Should().Contain(ConstraintPass.VetoReasonPrefix);
		music.BlockedReason.Should().Contain("plays_trombone");
	}

	[Fact]
	public void advisor_reports_a_restudy_bar_blocked_subject_as_unreachable()
	{
		var engine = Harness.ShippedEngine();

		var advice = engine.Advise(new("S-RESTUDY", new Dictionary<string, int> {
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
		}, []) { PriorQualifications = [new(Subject.Biology.Value, QualificationType.ALevel, "e")] });

		var biology = advice.Advice.Single(a => a.Subject == Subject.Biology);

		biology.Reachable.Should().BeFalse();
		biology.BlockedReason.Should().Contain(ConstraintPass.RestudyBarReasonPrefix);
		biology.BlockedReason.Should().Contain("biology");
	}

	[Fact]
	public void advisor_preserves_prior_qualifications_when_replaying_reported_changes()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-QUAL-EQUIV", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 7,
			["chemistry"] = 7,
			["biology"] = 4,
			["history"] = 7,
		}, []) {
			DateOfBirth = new DateOnly(2009, 9, 1),
			PriorQualifications = [new("applied_science", QualificationType.BtecDiploma, "distinction")],
		};

		var advice = engine.Advise(student);
		// Maths is red on its Maths-8 gate and reachable by a single bump. Biology (GCSE 4) is green only
		// because the applied_science distinction stands in for its entry and lifts its prediction — so if the
		// prior qualification were dropped while replaying the reported change, biology would fall to red.
		var maths = advice.Advice.Single(a => a.Subject == Subject.Maths);

		maths.Reachable.Should().BeTrue();

		var improved = ApplyChanges(student, maths.Changes);
		var explained = engine.Explain(improved);
		((int)explained.Explanations.Single(e => e.Subject == Subject.Maths).Rating).Should().BeLessThanOrEqualTo((int)maths.Target);
		explained.Explanations.Single(e => e.Subject == Subject.Biology).Rating.Should().Be(Rating.Green);
	}

	[Fact]
	public void advisor_does_not_claim_a_cap_blocked_green_is_reachable()
	{
		// The green cap is an optional feature, off in the shipped config; enable it here to exercise the
		// advisor's cap-blocked handling. Further Maths is red under the chosen-mode prerequisite (Maths is
		// not committed), so it leaves the green pool; the marginal cap-blocked green is now Computer Studies
		// (5th by priority weight behind Maths, Physics, Chemistry, Biology). No grade change can free a cap slot,
		// so it is unreachable.
		const int cap = 4;
		var rules = Harness.BuildFromShippedWorkflows().Engine;
		var engine = new EnrolmentEngine(rules, Harness.Thresholds with { MaxGreenChoices = cap }, Harness.Catalogue, Harness.AsOf);

		var advice = engine.Advise(StrongStudent("plays_piano"));
		var computerStudies = advice.Advice.Single(a => a.Subject == Subject.ComputerStudies);

		computerStudies.Reachable.Should().BeFalse();
		computerStudies.BlockedReason.Should().Contain(Aggregator.ExceedsCapReason);
	}

	[Fact]
	public void advisor_names_a_prerequisite_block_rather_than_blaming_the_grade_budget()
	{
		var engine = Harness.ShippedEngine();

		// A strong student who has not committed to Maths: Further Maths clears its own tier but is held red
		// by the chosen-Maths prerequisite, which no grade change can satisfy. The advisor must report that
		// it is unreachable for the prerequisite reason — not the misleading "budget exhausted".
		var advice = engine.Advise(StrongStudent());
		var furtherMaths = advice.Advice.Single(a => a.Subject == Subject.FurtherMaths);

		furtherMaths.Reachable.Should().BeFalse();
		furtherMaths.BlockedReason.Should().Be(ConstraintPass.MathsPrerequisiteReason);
	}

	[Fact]
	public void ineligible_student_gets_a_gate_clearing_bundle()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-INELIGIBLE", new Dictionary<string, int> { ["maths"] = 8, ["physics"] = 7, ["chemistry"] = 6 }, []);

		var advice = engine.Advise(student);

		advice.Eligible.Should().BeFalse();
		advice.EligibilityReasons.Should().Contain(reason => reason.Contains("passes", StringComparison.Ordinal));
		advice.Advice.Should().BeEmpty();
		advice.Gate.Should().NotBeNull();

		var improved = ApplyChanges(student, advice.Gate!.Changes);
		var explained = engine.Explain(improved);
		explained.Eligible.Should().BeTrue();
	}

	[Fact]
	public void gate_clearing_bundle_honours_a_loaded_min_passes_override()
	{
		// Raising the pass-count requirement via loaded data (no rebuild) must extend the gate-clearing
		// bundle: a five-pass student who clears the shipped gate needs a sixth pass under the override.
		var (_, rules) = Harness.BuildFromShippedWorkflows();
		var raised = Harness.Thresholds with { MinPasses = Harness.Thresholds.MinPasses + 1 };
		var engine = new EnrolmentEngine(rules, raised, Harness.AsOf);

		var student = new StudentInput("S-MINPASS", new Dictionary<string, int> {
			["english_language"] = Harness.Thresholds.PassGrade,
			["maths"] = Harness.Thresholds.PassGrade,
			["physics"] = Harness.Thresholds.PassGrade,
			["chemistry"] = Harness.Thresholds.PassGrade,
			["biology"] = Harness.Thresholds.PassGrade,
		}, []);

		var advice = engine.Advise(student);

		advice.Eligible.Should().BeFalse();
		advice.Gate.Should().NotBeNull();
		advice.Gate!.Changes.Should().NotBeEmpty();

		var improved = ApplyChanges(student, advice.Gate.Changes);
		engine.Explain(improved).Eligible.Should().BeTrue();
	}

	[Fact]
	public void budget_exhaustion_returns_unreachable()
	{
		var engine = Harness.ShippedEngine();

		// Maths is committed so Further Maths' chosen-Maths prerequisite is satisfied, isolating the grade
		// budget as the block. Further Maths needs both maths ≥ 7 and average ≥ 7. From an all-grade-4 start
		// the search can lift at most three subjects within the 12-grade-step budget (max average ≈ 6.4), so
		// the average entry can never be cleared ⇒ unreachable, blocked by the budget rather than an adjustment.
		var advice = engine.Advise(new("S-BUDGET", new Dictionary<string, int> {
			["english_language"] = 4,
			["maths"] = 4,
			["physics"] = 4,
			["chemistry"] = 4,
			["biology"] = 4,
		}, []) { ChosenALevels = [Subject.Maths] });

		var furtherMaths = advice.Advice.Single(a => a.Subject == Subject.FurtherMaths);
		furtherMaths.Reachable.Should().BeFalse();
		furtherMaths.BlockedReason.Should().NotBeNull();
		furtherMaths.BlockedReason!.Contains("budget", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
	}

	[Fact]
	public void cli_advise_matches_the_committed_golden()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "golden", "advise-counterfactual.json");
		var expectedPath = Path.Combine(Harness.RepoRoot, "examples", "golden", "advise-counterfactual.expected.json");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--advise", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();
		File.Exists(expectedPath).Should().BeTrue();

		var expected = File.ReadAllText(expectedPath);
		stdout.ToString().ReplaceLineEndings().TrimEnd()
			.Should().Be(expected.ReplaceLineEndings().TrimEnd());

		var parsed = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.AdviceResult);
		parsed.Should().NotBeNull();
		parsed!.Advice.Should().NotBeEmpty();

		// Hand-checked invariants beside the byte-match.
		parsed.Eligible.Should().BeTrue();
		parsed.Gate.Should().BeNull();
		parsed.TruncationReason.Should().BeNull();

		var furtherMaths = parsed.Advice.Single(a => a.Subject == Subject.FurtherMaths);
		furtherMaths.Reachable.Should().BeFalse();
		furtherMaths.BlockedReason.Should().Be(ConstraintPass.MathsPrerequisiteReason);

		var maths = parsed.Advice.Single(a => a.Subject == Subject.Maths);
		maths.Reachable.Should().BeTrue();
		maths.Changes.Should().ContainSingle(c => c.GcseSubject == "maths" && c.From == 5 && c.To == 8);
	}

	[Fact]
	public void reachable_cli_golden_advice_changes_attain_their_stated_targets()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "golden", "advise-counterfactual.json");
		using var stream = File.OpenRead(path);
		var student = JsonSerializer.Deserialize(stream, EnrolmentJsonContext.Default.StudentDocument)!.Student;
		var engine = Harness.ShippedEngine();
		var advice = engine.Advise(student);

		foreach (var subjectAdvice in advice.Advice.Where(static subjectAdvice => subjectAdvice.Reachable)) {
			var improved = ApplyChanges(student, subjectAdvice.Changes);
			var explained = engine.Explain(improved);
			((int)explained.Explanations.Single(explanation => explanation.Subject == subjectAdvice.Subject).Rating)
				.Should()
				.BeLessThanOrEqualTo((int)subjectAdvice.Target);
		}
	}

	[Fact]
	public void cli_advise_all_gcses_flag_enables_the_diagnostic_search()
	{
		var dir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, "student.json");
		File.WriteAllText(path, """
								{ "student": { "id": "S-CLI-DIAG",
								  "gcses": {"english_language":7,"maths":5,"physics":5,"chemistry":5,"biology":5},
								  "hobbies": [], "chosen_a_levels": ["maths"], "date_of_birth": "2009-09-01" } }
								""");

		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = CliRunner.Run(["--advise", "--all-gcses", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();
		var parsed = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.AdviceResult);
		// Diagnostic mode: Spanish, gated on a French/German GCSE the student never sat, is reachable by
		// proposing one — the flag flipped the candidate set on through the CLI.
		parsed!.Advice.Single(a => a.Subject == Subject.Spanish).Reachable.Should().BeTrue();
	}

	[Fact]
	public void low_grade_cost_budget_marks_subject_unreachable()
	{
		var (_, rules) = Harness.BuildFromShippedWorkflows();
		var engine = new EnrolmentEngine(
			rules, Harness.Thresholds with { AdviceMaxGradeCost = 1 }, Harness.Catalogue, Harness.AsOf);
		var student = new StudentInput("S-ADVISE", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []);

		var advice = engine.Advise(student);
		// Maths needs a 3-grade bump (5 → 8) to clear its entry gate; a 1-grade budget cannot reach it.
		var maths = advice.Advice.Single(a => a.Subject == Subject.Maths);

		maths.Reachable.Should().BeFalse();
		maths.BlockedReason.Should().Be("budget exhausted");
	}

	[Fact]
	public void pipeline_evaluation_cap_truncates_advice()
	{
		var (_, rules) = Harness.BuildFromShippedWorkflows();
		var student = new StudentInput("S-ADVISE", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []);

		// Evaluate without the cap to establish the expected full advice size.
		var uncapped = new EnrolmentEngine(
			rules, Harness.Thresholds, Harness.Catalogue, Harness.AsOf);
		var fullAdvice = uncapped.Advise(student);

		// Evaluate with an aggressive cap: 1 pipeline evaluation.
		var capped = new EnrolmentEngine(
			rules, Harness.Thresholds with { AdviceMaxPipelineEvaluations = 1 }, Harness.Catalogue, Harness.AsOf);
		var truncated = capped.Advise(student);

		truncated.TruncationReason.Should().Be("advice truncated");
		truncated.Advice.Count.Should().BeLessThan(fullAdvice.Advice.Count);
	}

	[Fact]
	public void advise_honours_cancellation_at_the_entry_guard()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-CANCEL", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []);

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		// An already-cancelled token is observed at the first ThrowIfCancellationRequested in the advisor
		// entry guard, before any search state is allocated.
		var act = () => engine.Advise(student, true, cts.Token);
		act.Should().Throw<OperationCanceledException>();
	}

	[Fact]
	public void advise_honours_cancellation_during_the_search()
	{
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-CANCEL", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []);

		// Deterministic, no wall clock: trip the token synchronously from the per-evaluation hook
		// once the search has done a few pipeline evaluations. The budget hook fires only inside the
		// per-subject search (the entry guard and ExplainAsync run before any evaluation is consumed),
		// so observing >= TripAfter evaluations before the throw proves cancellation was seen inside
		// the BFS loop, not merely at the entry guard. Replaces the original 1ms/500ms timer race.
		const int TripAfter = 3;
		using var cts = new CancellationTokenSource();
		var evaluations = 0;

		void OnEvaluation()
		{
			if (++evaluations >= TripAfter) {
				cts.Cancel();
			}
		}

		var act = () => CounterfactualAdvisor.Advise(
			engine, student, Harness.Thresholds, Harness.AsOf, true, OnEvaluation, cts.Token);
		act.Should().Throw<OperationCanceledException>();
		evaluations.Should().BeGreaterThanOrEqualTo(TripAfter);
	}

	private static StudentInput ApplyChanges(StudentInput original, EquatableArray<GradeChange> changes)
	{
		var gcses = original.Gcses?.ToDictionary(static kv => kv.Key, static kv => kv.Value) ?? [];
		foreach (var change in changes) {
			gcses[change.GcseSubject] = change.To;
		}

		return original with { Gcses = EquatableDictionaryFactory.CopyOf(gcses) };
	}
}
