namespace EnrolmentRules.Tests;

using System.Text.Json;
using Cli;
using Domain;
using Engine;
using FluentAssertions;

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
	public async Task advisor_finds_the_smallest_bump_that_flips_a_red_subject()
	{
		var engine = await Harness.ShippedEngineAsync();

		var advice = await engine.AdviseAsync(new("S-ADVISE", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []));
		var chemistry = advice.Advice.Single(a => a.Subject == Subject.Chemistry);

		chemistry.Reachable.Should().BeTrue();
		chemistry.Target.Should().Be(Rating.Amber);
		chemistry.Changes.Should().NotBeEmpty();

		var improved = ApplyChanges(new("S-ADVISE", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []), chemistry.Changes);
		var explained = await engine.ExplainAsync(improved);
		explained.Explanations.Single(e => e.Subject == Subject.Chemistry).Rating.Should().Be(Rating.Amber);
	}

	[Fact]
	public async Task advice_never_proposes_sitting_a_gcse_the_student_has_not_taken()
	{
		var engine = await Harness.ShippedEngineAsync();
		var student = new StudentInput("S-HELD-ONLY", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []);

		var advice = await engine.AdviseAsync(student);

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
	public async Task advisor_considers_unsat_gcses_when_asked_for_diagnosis()
	{
		var engine = await Harness.ShippedEngineAsync();
		// Maths is committed so Further Maths' prerequisite is met and its search returns early rather than
		// exhausting the (now 13-wide) diagnostic candidate space — keeps this slow-by-design path quick here.
		var student = new StudentInput("S-HELD-ONLY", new Dictionary<string, int> {
			["english_language"] = 7,
			["maths"] = 5,
			["physics"] = 5,
			["chemistry"] = 5,
			["biology"] = 5,
		}, []) { ChosenALevels = [Subject.Maths] };

		var advice = await engine.AdviseAsync(student, true);

		// Diagnostic mode reverts to the old, heavier behaviour: Spanish, gated on a French or German GCSE
		// the student never sat, becomes reachable by proposing those brand-new GCSEs.
		advice.Advice.Single(a => a.Subject == Subject.Spanish).Reachable.Should().BeTrue();
		advice.Advice
			.SelectMany(static a => a.Changes)
			.Select(static c => c.GcseSubject)
			.Should().Contain("french");
	}

	[Fact]
	public async Task advice_unsat_gcse_behaviour_defaults_from_thresholds()
	{
		var (_, rules) = await Harness.BuildFromShippedWorkflowsAsync();
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
		var advice = await engine.AdviseAsync(student);

		advice.Advice.Single(a => a.Subject == Subject.Spanish).Reachable.Should().BeTrue();
	}

	[Fact]
	public async Task advisor_reports_a_veto_blocked_subject_as_unreachable()
	{
		var engine = await Harness.ShippedEngineAsync();

		var advice = await engine.AdviseAsync(StrongStudent("plays_trombone"));
		var music = advice.Advice.Single(a => a.Subject == Subject.Music);

		music.Reachable.Should().BeFalse();
		music.BlockedReason.Should().Contain(ConstraintPass.VetoReasonPrefix);
		music.BlockedReason.Should().Contain("plays_trombone");
	}

	[Fact]
	public async Task advisor_reports_a_restudy_bar_blocked_subject_as_unreachable()
	{
		var engine = await Harness.ShippedEngineAsync();

		var advice = await engine.AdviseAsync(new("S-RESTUDY", new Dictionary<string, int> {
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
	public async Task advisor_does_not_claim_a_cap_blocked_green_is_reachable()
	{
		// The green cap is an optional feature, off in the shipped config; enable it here to exercise the
		// advisor's cap-blocked handling. Further Maths is red under the chosen-mode prerequisite (Maths is
		// not committed), so it leaves the green pool; the marginal cap-blocked green is now Computer Studies
		// (5th by UCAS weight behind Maths, Physics, Chemistry, Biology). No grade change can free a cap slot,
		// so it is unreachable.
		const int cap = 4;
		var rules = (await Harness.BuildFromShippedWorkflowsAsync()).Engine;
		var engine = new EnrolmentEngine(rules, Harness.Thresholds with { MaxGreenChoices = cap }, Harness.Catalogue, Harness.AsOf);

		var advice = await engine.AdviseAsync(StrongStudent("plays_piano"));
		var computerStudies = advice.Advice.Single(a => a.Subject == Subject.ComputerStudies);

		computerStudies.Reachable.Should().BeFalse();
		computerStudies.BlockedReason.Should().Contain(Aggregator.ExceedsCapReason);
	}

	[Fact]
	public async Task advisor_names_a_prerequisite_block_rather_than_blaming_the_grade_budget()
	{
		var engine = await Harness.ShippedEngineAsync();

		// A strong student who has not committed to Maths: Further Maths clears its own tier but is held red
		// by the chosen-Maths prerequisite, which no grade change can satisfy. The advisor must report that
		// it is unreachable for the prerequisite reason — not the misleading "budget exhausted".
		var advice = await engine.AdviseAsync(StrongStudent());
		var furtherMaths = advice.Advice.Single(a => a.Subject == Subject.FurtherMaths);

		furtherMaths.Reachable.Should().BeFalse();
		furtherMaths.BlockedReason.Should().Be(ConstraintPass.MathsPrerequisiteReason);
	}

	[Fact]
	public async Task ineligible_student_gets_a_gate_clearing_bundle()
	{
		var engine = await Harness.ShippedEngineAsync();
		var student = new StudentInput("S-INELIGIBLE", new Dictionary<string, int> { ["maths"] = 8, ["physics"] = 7, ["chemistry"] = 6 }, []);

		var advice = await engine.AdviseAsync(student);

		advice.Eligible.Should().BeFalse();
		advice.EligibilityReasons.Should().Contain(reason => reason.Contains("passes", StringComparison.Ordinal));
		advice.Advice.Should().BeEmpty();
		advice.Gate.Should().NotBeNull();

		var improved = ApplyChanges(student, advice.Gate!.Changes);
		var explained = await engine.ExplainAsync(improved);
		explained.Eligible.Should().BeTrue();
	}

	[Fact]
	public async Task gate_clearing_bundle_honours_a_loaded_min_passes_override()
	{
		// Raising the pass-count requirement via loaded data (no rebuild) must extend the gate-clearing
		// bundle: a five-pass student who clears the shipped gate needs a sixth pass under the override.
		var (_, rules) = await Harness.BuildFromShippedWorkflowsAsync();
		var raised = Harness.Thresholds with { MinPasses = Harness.Thresholds.MinPasses + 1 };
		var engine = new EnrolmentEngine(rules, raised, Harness.AsOf);

		var student = new StudentInput("S-MINPASS", new Dictionary<string, int> {
			["english_language"] = Harness.Thresholds.PassGrade,
			["maths"] = Harness.Thresholds.PassGrade,
			["physics"] = Harness.Thresholds.PassGrade,
			["chemistry"] = Harness.Thresholds.PassGrade,
			["biology"] = Harness.Thresholds.PassGrade,
		}, []);

		var advice = await engine.AdviseAsync(student);

		advice.Eligible.Should().BeFalse();
		advice.Gate.Should().NotBeNull();
		advice.Gate!.Changes.Should().NotBeEmpty();

		var improved = ApplyChanges(student, advice.Gate.Changes);
		(await engine.ExplainAsync(improved)).Eligible.Should().BeTrue();
	}

	[Fact]
	public async Task budget_exhaustion_returns_unreachable()
	{
		var engine = await Harness.ShippedEngineAsync();

		// Maths is committed so Further Maths' chosen-Maths prerequisite is satisfied, isolating the grade
		// budget as the block. Further Maths needs both maths ≥ 7 and average ≥ 7. From an all-grade-4 start
		// the search can lift at most three subjects within the 12-grade-step budget (max average ≈ 6.4), so
		// the average entry can never be cleared ⇒ unreachable, blocked by the budget rather than an adjustment.
		var advice = await engine.AdviseAsync(new("S-BUDGET", new Dictionary<string, int> {
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
	public async Task cli_advise_matches_the_committed_golden()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "golden", "advise-counterfactual.json");
		var expectedPath = Path.Combine(Harness.RepoRoot, "examples", "golden", "advise-counterfactual.expected.json");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--advise", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();
		File.Exists(expectedPath).Should().BeTrue();

		var expected = await File.ReadAllTextAsync(expectedPath);
		stdout.ToString().ReplaceLineEndings().TrimEnd()
			.Should().Be(expected.ReplaceLineEndings().TrimEnd());

		var parsed = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.AdviceResult);
		parsed.Should().NotBeNull();
		parsed!.Advice.Should().NotBeEmpty();
	}

	[Fact]
	public async Task cli_advise_all_gcses_flag_enables_the_diagnostic_search()
	{
		var dir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, "student.json");
		await File.WriteAllTextAsync(path, """
										   { "student": { "id": "S-CLI-DIAG",
										     "gcses": {"english_language":7,"maths":5,"physics":5,"chemistry":5,"biology":5},
										     "hobbies": [], "chosen_a_levels": ["maths"], "date_of_birth": "2009-09-01" } }
										   """);

		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--advise", "--all-gcses", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		stderr.ToString().Should().BeEmpty();
		var parsed = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.AdviceResult);
		// Diagnostic mode: Spanish, gated on a French/German GCSE the student never sat, is reachable by
		// proposing one — the flag flipped the candidate set on through the CLI.
		parsed!.Advice.Single(a => a.Subject == Subject.Spanish).Reachable.Should().BeTrue();
	}

	private static StudentInput ApplyChanges(StudentInput original, EquatableArray<GradeChange> changes)
	{
		var gcses = original.Gcses?.ToDictionary(static kv => kv.Key, static kv => kv.Value) ?? new Dictionary<string, int>();
		foreach (var change in changes) {
			gcses[change.GcseSubject] = change.To;
		}

		return new(original.Id, gcses, original.Hobbies) { ChosenALevels = original.ChosenALevels };
	}
}
