namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Phase 7 — the golden-file end-to-end suite: a handful of representative students whose expected
///     <see cref="EnrolmentResult" /> JSON is committed alongside the input. Each fixture is evaluated
///     through the whole pipeline (predict → engine → constraint pass → cap → aggregate) and compared
///     byte-for-byte against its golden. This is the primary defence against the untyped-rule risk
///     (Reservation 1): a silent rule error breaks a golden file. Hand-checked invariants per fixture sit
///     beside the byte-match so the goldens are not merely self-consistent.
///     To regenerate goldens after a deliberate output change: delete the *.expected.json files, run
///     the fixture_evaluates_to_its_committed_golden test (which will fail with "file must be committed"),
///     copy each actual output into the corresponding expected path, and commit alongside the change.
///     Review every diff in the regenerated golden — a golden committed without scrutiny defeats its own
///     purpose.
/// </summary>
public sealed class GoldenFileTests
{
	private const string ExpectedSuffix = ".expected.json";
	private static string GoldenDir => Path.Combine(Harness.RepoRoot, "examples", "golden");

	public static TheoryData<string> Fixtures()
	{
		var data = new TheoryData<string>();
		foreach (var file in Directory.EnumerateFiles(GoldenDir, "*.json").OrderBy(static f => f, StringComparer.Ordinal)) {
			if (!file.EndsWith(ExpectedSuffix, StringComparison.Ordinal) && !file.Contains("advise-counterfactual", StringComparison.Ordinal)) {
				data.Add(Path.GetFileNameWithoutExtension(file));
			}
		}

		return data;
	}

	private static EnrolmentResult EvaluateFixture(string fixture)
	{
		var engine = Harness.ShippedEngine();
		return engine.Evaluate(LoadFixture(fixture));
	}

	private static StudentInput LoadFixture(string fixture)
	{
		using var stream = File.OpenRead(Path.Combine(GoldenDir, fixture + ".json"));
		return JsonSerializer.Deserialize(stream, EnrolmentJsonContext.Default.StudentDocument)!.Student;
	}

	[Theory]
	[MemberData(nameof(Fixtures))]
	public void fixture_evaluates_to_its_committed_golden(string fixture)
	{
		var result = EvaluateFixture(fixture);
		var actual = JsonSerializer.Serialize(result, EnrolmentJsonContext.Default.EnrolmentResult);

		var expectedPath = Path.Combine(GoldenDir, fixture + ExpectedSuffix);
		File.Exists(expectedPath).Should().BeTrue($"golden file '{fixture}{ExpectedSuffix}' must be committed");

		var expected = File.ReadAllText(expectedPath);
		actual.ReplaceLineEndings().TrimEnd().Should().Be(expected.ReplaceLineEndings().TrimEnd());
	}

	[Fact]
	public void top_allrounder_is_eligible_with_no_constraint_downgrades()
	{
		// The realistic mid-strong student (avg ≈ 6.78): a few greens, several ambers, Further Maths
		// red on its own avg ≥ 7 entry rule, and untaken added subjects red. No both-green clash, so the
		// host-code pass makes no adjustments.
		var result = EvaluateFixture("top-allrounder");

		result.Eligible.Should().BeTrue();
		result.Recommendations.Single(r => r.Subject == Subject.FurtherMaths).Rating.Should().Be(Rating.Red);
		result.Adjustments.Should().BeEmpty();
	}

	[Fact]
	public void ineligible_fixture_is_red_everywhere_with_the_gate_reason()
	{
		var result = EvaluateFixture("ineligible-no-english");

		result.Eligible.Should().BeFalse();
		result.Recommendations.Should().OnlyContain(r => r.Rating == Rating.Red);
		result.Recommendations.Should().OnlyContain(r => r.Reason.Contains("English", StringComparison.Ordinal));
		result.Summary.Should().Be(new EnrolmentSummary(0, 0, 0.0));
		result.Adjustments.Should().BeEmpty();
	}

	[Fact]
	public void strong_constraints_fixture_exercises_exclusion_and_own_time()
	{
		// All eights ⇒ every subject green at base; the host-code pass then fires the exclusion, own-time and
		// prerequisite (Further Maths needs a committed Maths) downgrades. The green cap is disabled in the
		// shipped config, so the surviving greens are NOT clamped — every legitimate green stays green.
		var result = EvaluateFixture("strong-constraints");

		result.Eligible.Should().BeTrue();
		Harness.Thresholds.MaxGreenChoices.Should().BeNull();
		result.Adjustments.Should().NotContain(a => a.Reason == Aggregator.ExceedsCapReason);

		var art = result.Recommendations.Single(r => r.Subject == Subject.Art);
		art.Rating.Should().Be(Rating.Amber);
		art.Reason.Should().Contain("Mutual exclusion").And.Contain(EnumNames.NameOf(Subject.History));

		var music = result.Recommendations.Single(r => r.Subject == Subject.Music);
		music.Rating.Should().Be(Rating.Amber);
		music.Reason.Should().Be(ConstraintPass.OwnTimeReason);
	}

	[Fact]
	public void chosen_prior_choice_red_fixture_excludes_german_and_downgrades_french()
	{
		// An all-8s student (except German at 5) who has committed to German A-level: French
		// is excluded as a prior choice, Art is demoted by mutual exclusion with History,
		// and German itself is red from its own low grade.
		var result = EvaluateFixture("chosen-prior-choice-red");

		result.Eligible.Should().BeTrue();

		var french = result.Recommendations.Single(r => r.Subject == Subject.French);
		french.Rating.Should().Be(Rating.Red);
		french.Reason.Should().Be("Cannot be combined with chosen german");

		var german = result.Recommendations.Single(r => r.Subject == Subject.German);
		german.Rating.Should().Be(Rating.Red);
		german.Reason.Should().Contain("amber threshold");

		var art = result.Recommendations.Single(r => r.Subject == Subject.Art);
		art.Rating.Should().Be(Rating.Amber);
		art.Reason.Should().Contain("Mutual exclusion").And.Contain("history");

		result.Adjustments.Should().ContainSingle(a =>
			a.Subject == Subject.French && a.From == Rating.Green && a.To == Rating.Red
			&& a.Reason.Contains("Cannot be combined with chosen german"));
	}

	[Fact]
	public void green_cap_when_enabled_clamps_the_strong_constraints_fixture_end_to_end()
	{
		// The optional green cap is off in the shipped config. Opting it in (a one-line data change, no
		// rebuild) must clamp the surviving greens end-to-end: the all-eights student keeps exactly the cap
		// many greens, the lowest-weight surplus demoted to amber with the cap reason.
		const int cap = 4;
		using var stream = File.OpenRead(Path.Combine(GoldenDir, "strong-constraints.json"));
		var document = JsonSerializer.Deserialize(stream, EnrolmentJsonContext.Default.StudentDocument)!;
		var rules = Harness.BuildFromShippedWorkflows().Engine;
		var cappedEngine = new EnrolmentEngine(rules, Harness.Thresholds with { MaxGreenChoices = cap }, Harness.Catalogue, Harness.AsOf);

		var result = cappedEngine.Evaluate(document.Student);

		result.Eligible.Should().BeTrue();
		result.Summary.GreenCount.Should().Be(cap);
		result.Adjustments.Should().Contain(a => a.Reason == Aggregator.ExceedsCapReason);
	}

	[Fact]
	public void adult_art_boundary_fixture_uses_the_fixed_as_of_date_for_the_age_gate()
	{
		var engine = Harness.ShippedEngine();
		var adult = new StudentInput(
			"S-ADULT-ART",
			new Dictionary<string, int> {
				["english_language"] = 9,
				["maths"] = 5,
				["physics"] = 9,
				["chemistry"] = 9,
				["art"] = 6,
			},
			[]) { DateOfBirth = new DateOnly(2007, 9, 1) };
		var younger = adult with { DateOfBirth = adult.DateOfBirth!.Value.AddDays(1) };

		var adultExplanation = engine.Explain(adult);
		var youngerExplanation = engine.Explain(younger);

		adultExplanation.Eligible.Should().BeTrue();
		Harness.AsOf.Should().Be(new(2026, 9, 1));
		adultExplanation.Explanations.Single(explanation => explanation.Subject == Subject.Art).BaseRating.Should().Be(Rating.Red);
		youngerExplanation.Explanations.Single(explanation => explanation.Subject == Subject.Art).BaseRating.Should().NotBe(Rating.Red);
	}

	[Fact]
	public void prior_equivalent_fixture_opens_biology_upstream_before_the_restudy_bar_downgrades_it()
	{
		var engine = Harness.ShippedEngine();
		var fixture = LoadFixture("prior-equivalent-restudy-bar");
		var equivalentOnly = fixture with { PriorQualifications = [fixture.PriorQualifications[0]] };

		var equivalentOnlyResult = engine.Evaluate(equivalentOnly);
		var combinedResult = engine.Evaluate(fixture);

		equivalentOnlyResult.Recommendations.Single(r => r.Subject == Subject.Biology).Rating.Should().Be(Rating.Green);

		var biology = combinedResult.Recommendations.Single(r => r.Subject == Subject.Biology);
		biology.Rating.Should().Be(Rating.Red);
		biology.Reason.Should().StartWith(ConstraintPass.RestudyBarReasonPrefix);
		combinedResult.Adjustments.Should().ContainSingle(adjustment =>
			adjustment.Subject == Subject.Biology
			&& adjustment.From == Rating.Green
			&& adjustment.To == Rating.Red
			&& adjustment.Reason.StartsWith(ConstraintPass.RestudyBarReasonPrefix, StringComparison.Ordinal));
	}

	[Fact]
	public void veto_fixture_shows_music_as_otherwise_green_but_barred_by_activity()
	{
		var engine = Harness.ShippedEngine();
		var fixture = LoadFixture("veto-precedence");
		var withoutVeto = fixture with { Hobbies = ["plays_piano"] };

		var withoutVetoResult = engine.Evaluate(withoutVeto);
		var withVetoResult = engine.Evaluate(fixture);

		withoutVetoResult.Recommendations.Single(r => r.Subject == Subject.Music).Rating.Should().Be(Rating.Green);

		var music = withVetoResult.Recommendations.Single(r => r.Subject == Subject.Music);
		music.Rating.Should().Be(Rating.Red);
		music.Reason.Should().StartWith(ConstraintPass.VetoReasonPrefix).And.Contain("plays_trombone");
		withVetoResult.Adjustments.Should().ContainSingle(adjustment =>
			adjustment.Subject == Subject.Music
			&& adjustment.From == Rating.Green
			&& adjustment.To == Rating.Red
			&& adjustment.Reason.StartsWith(ConstraintPass.VetoReasonPrefix, StringComparison.Ordinal));
	}
}
