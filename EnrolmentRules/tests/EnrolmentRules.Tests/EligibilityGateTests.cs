namespace EnrolmentRules.Tests;

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Domain;
using Engine;
using Prediction;
using RulesEngine.Interfaces;

/// <summary>
///     Phase 2 — the §1.3 eligibility gate as a rules-as-data workflow, driven <em>through the engine</em>
///     (never by eyeballing JSON). Pins the boundary matrix, the reason precedence, the array-vs-accessor
///     input shaping, and the hot-swap property the whole engine choice rests on.
/// </summary>
public sealed class EligibilityGateTests
{
	private static string DataDir => Path.Combine(Harness.RepoRoot, "data");

	private static GcseResult[] Gcses(params (string Subject, int Grade)[] gcses) =>
		[.. gcses.Select(g => new GcseResult(g.Subject, g.Grade))];

	private static StudentInput ExampleStudent()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "student.json");
		var json = File.ReadAllText(path);
		var document = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentDocument);
		return document!.Student;
	}

	[Fact]
	public void shipped_thresholds_load_from_yaml_and_match_the_named_policy()
	{
		var thresholds = PolicyThresholdsStore.LoadAndValidate(DataDir);

		thresholds.PassGrade.Should().Be(4);
		thresholds.MinPasses.Should().Be(5);
		thresholds.TopEntry.Should().Be(7);
		thresholds.StrongEntry.Should().Be(6);
		thresholds.StandardEntry.Should().Be(5);
		thresholds.FurtherMathsAverageEntry.Should().BeApproximately(7.0, 1e-9);
		thresholds.HumanitiesAverageEntry.Should().BeApproximately(5.0, 1e-9);
		thresholds.MinDfeGreenProbabilityAtOrAbove.Should().BeApproximately(0.60, 1e-9);
		thresholds.MinDfeAmberProbabilityAtOrAbove.Should().BeApproximately(0.50, 1e-9);
		thresholds.AdultAge.Should().Be(19);
		thresholds.MaxGreenChoices.Should().BeNull(); // green cap is an optional feature, disabled in the shipped config
		thresholds.AmberTariffFactor.Should().BeApproximately(0.5, 1e-9);
		thresholds.AdviceConsidersUnsatGcses.Should().BeFalse(); // diagnostic advisor knob, off in the shipped config
	}

	[Fact]
	public void advice_considers_unsat_gcses_is_optional_and_defaults_off_when_absent()
	{
		var schema = File.ReadAllText(Path.Combine(DataDir, PolicyThresholdsStore.SchemaFileName));

		// A thresholds document that omits the optional knob entirely must still validate and default to off.
		const string withoutKnob = """
								   pass_grade: 4
								   min_passes: 5
								   top_entry: 7
								   strong_entry: 6
								   standard_entry: 5
								   further_maths_average_entry: 7.0
								   humanities_average_entry: 5.0
								   min_dfe_green_probability_at_or_above: 0.60
								   min_dfe_amber_probability_at_or_above: 0.50
								   adult_age: 19
								   amber_tariff_factor: 0.5
								   """;

		var defaulted = PolicyThresholdsStore.LoadAndValidate(new StringReader(withoutKnob), new StringReader(schema));
		defaulted.AdviceConsidersUnsatGcses.Should().BeFalse();

		var enabled = PolicyThresholdsStore.LoadAndValidate(
			new StringReader(withoutKnob + "\nadvice_considers_unsat_gcses: true"), new StringReader(schema));
		enabled.AdviceConsidersUnsatGcses.Should().BeTrue();
	}

	[Fact]
	public async Task exactly_min_passes_including_english_and_maths_is_eligible()
	{
		// Five passes (≥ PassGrade), English and Maths among them: the minimum eligible student.
		var gcses = Gcses(
			("english_language", Harness.Thresholds.PassGrade), ("maths", Harness.Thresholds.PassGrade),
			("physics", Harness.Thresholds.PassGrade), ("chemistry", Harness.Thresholds.PassGrade),
			("biology", Harness.Thresholds.PassGrade));

		var evaluator = await Harness.ShippedEvaluatorAsync();
		var gate = await evaluator.EvaluateEligibilityAsync(gcses);

		gate.Eligible.Should().BeTrue();
		gate.Reasons.Should().BeEmpty();
	}

	[Fact]
	public async Task missing_english_is_ineligible_with_the_english_reason()
	{
		// Five passes, but English absent — absent ⇒ not a pass, so only the English requirement fails.
		var gcses = Gcses(
			("maths", Harness.Thresholds.PassGrade), ("physics", Harness.Thresholds.PassGrade),
			("chemistry", Harness.Thresholds.PassGrade), ("biology", Harness.Thresholds.PassGrade),
			("history", Harness.Thresholds.PassGrade));

		var evaluator = await Harness.ShippedEvaluatorAsync();
		var gate = await evaluator.EvaluateEligibilityAsync(gcses);

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().ContainSingle().Which.Should().Contain("English");
	}

	[Fact]
	public async Task english_failure_reason_uses_the_loaded_pass_grade()
	{
		var thresholds = Harness.Thresholds with { PassGrade = 7 };
		var evaluator = await EvaluatorAsync(thresholds);

		var gate = await evaluator.EvaluateEligibilityAsync(Gcses(
			("english_language", 6), ("maths", 7), ("physics", 7), ("chemistry", 7), ("biology", 7),
			("history", 7), ("geography", 7)));

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().Equal("GCSE English Language below the pass grade (7)");
	}

	[Fact]
	public async Task maths_failure_reason_uses_the_loaded_pass_grade()
	{
		var thresholds = Harness.Thresholds with { PassGrade = 7 };
		var evaluator = await EvaluatorAsync(thresholds);

		var gate = await evaluator.EvaluateEligibilityAsync(Gcses(
			("english_language", 7), ("maths", 6), ("physics", 7), ("chemistry", 7), ("biology", 7),
			("history", 7), ("geography", 7)));

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().Equal("GCSE Maths below the pass grade (7)");
	}

	[Fact]
	public async Task failing_english_and_pass_count_lists_english_first()
	{
		// Only Maths present: English fails, pass-count fails, Maths passes. Precedence puts English first.
		var evaluator = await Harness.ShippedEvaluatorAsync();
		var gate = await evaluator.EvaluateEligibilityAsync(Gcses(("maths", Harness.Thresholds.PassGrade)));

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().HaveCount(2);
		gate.Reasons[0].Should().Contain("English");
		gate.Reasons[1].Should().Contain("passes");
	}

	[Fact]
	public async Task multiple_failures_keep_declared_order_with_loaded_threshold_values()
	{
		var thresholds = Harness.Thresholds with { PassGrade = 7, MinPasses = 6 };
		var evaluator = await EvaluatorAsync(thresholds);

		var gate = await evaluator.EvaluateEligibilityAsync(Gcses(("maths", 7), ("physics", 7), ("chemistry", 7)));

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().Equal(
			"GCSE English Language below the pass grade (7)",
			"Fewer than the required number of GCSE passes (6 at grade 7 or above)");
	}

	[Fact]
	public async Task grades_below_pass_grade_are_excluded_from_the_count()
	{
		// English + Maths pass, but the other three sit one below PassGrade, so only two count: ineligible.
		var below = Harness.Thresholds.PassGrade - 1;
		var gcses = Gcses(
			("english_language", Harness.Thresholds.PassGrade), ("maths", Harness.Thresholds.PassGrade),
			("physics", below), ("chemistry", below), ("biology", below));

		var evaluator = await Harness.ShippedEvaluatorAsync();
		var gate = await evaluator.EvaluateEligibilityAsync(gcses);

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().ContainSingle().Which.Should().Contain("passes");
	}

	[Fact]
	public async Task pass_count_failure_reason_uses_the_loaded_thresholds()
	{
		var thresholds = Harness.Thresholds with { PassGrade = 7, MinPasses = 6 };
		var evaluator = await EvaluatorAsync(thresholds);

		var gate = await evaluator.EvaluateEligibilityAsync(Gcses(
			("english_language", 7), ("maths", 7), ("physics", 7), ("chemistry", 7), ("biology", 7)));

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().Equal("Fewer than the required number of GCSE passes (6 at grade 7 or above)");
	}

	[Fact]
	public async Task grades_at_pass_grade_are_counted()
	{
		// Same five subjects, now all exactly at PassGrade: the boundary is inclusive, so eligible.
		var gcses = Gcses(
			("english_language", Harness.Thresholds.PassGrade), ("maths", Harness.Thresholds.PassGrade),
			("physics", Harness.Thresholds.PassGrade), ("chemistry", Harness.Thresholds.PassGrade),
			("biology", Harness.Thresholds.PassGrade));

		var evaluator = await Harness.ShippedEvaluatorAsync();
		var gate = await evaluator.EvaluateEligibilityAsync(gcses);

		gate.Eligible.Should().BeTrue();
	}

	[Fact]
	public async Task pass_count_binds_to_the_array_not_a_deduplicated_lookup()
	{
		// A list with duplicate passing entries: the array counts each (≥ MinPasses ⇒ eligible). Were the
		// count re-pointed at the keyed accessor it would collapse the duplicates and fall under the bar.
		var gcses = Gcses(
			("english_language", Harness.Thresholds.PassGrade), ("maths", Harness.Thresholds.PassGrade),
			("physics", Harness.Thresholds.PassGrade), ("physics", Harness.Thresholds.PassGrade),
			("physics", Harness.Thresholds.PassGrade));

		// Sanity-check the premise: the keyed accessor would collapse the three Physics entries to one.
		new GcseFacts(gcses).Grade("physics").Should().Be(Harness.Thresholds.PassGrade);

		var evaluator = await Harness.ShippedEvaluatorAsync();
		var gate = await evaluator.EvaluateEligibilityAsync(gcses);

		gate.Eligible.Should().BeTrue();
	}

	[Fact]
	public async Task threshold_is_editable_in_yaml_without_a_recompile()
	{
		// Hot-swap: raise the pass-count threshold in a copied thresholds file. A student that was exactly
		// eligible becomes ineligible — the rules-as-data property, no rebuild.
		var fiveExactPasses = Gcses(
			("english_language", Harness.Thresholds.PassGrade), ("maths", Harness.Thresholds.PassGrade),
			("physics", Harness.Thresholds.PassGrade), ("chemistry", Harness.Thresholds.PassGrade),
			("biology", Harness.Thresholds.PassGrade));

		var shippedThresholds = PolicyThresholdsStore.LoadAndValidate(DataDir);
		var raisedThresholds = shippedThresholds with { MinPasses = shippedThresholds.MinPasses + 1 };

		var workflows = WorkflowStore.LoadAndValidate(Harness.WorkflowsDir, Harness.SchemaPath);
		var engine = WorkflowStore.BuildEngine(workflows);
		await WorkflowStore.ProbeCompileAsync(engine, workflows, Harness.CanonicalProbe(raisedThresholds));
		var evaluator = new RatingEvaluator(engine, raisedThresholds);

		var gate = await evaluator.EvaluateEligibilityAsync(fiveExactPasses);

		gate.Eligible.Should().BeFalse();
		gate.Reasons.Should().ContainSingle().Which.Should().Contain("passes");
	}

	[Fact]
	public async Task shipped_eligibility_workflow_probe_compiles()
	{
		// The boot guard: the real eligibility lambdas compile and bind against the canonical probe.
		var (workflows, engine) = await Harness.BuildFromShippedWorkflowsAsync();

		var act = async () => await WorkflowStore.ProbeCompileAsync(engine, workflows, Harness.CanonicalProbe());

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public void public_constructors_do_not_expose_the_rulesengine_dependency()
	{
		typeof(EnrolmentEngine).GetConstructors()
			.Where(static constructor =>
				constructor.GetParameters().Any(static parameter => parameter.ParameterType == typeof(IRulesEngine)))
			.Should()
			.BeEmpty();
	}

	[Fact]
	public void the_subject_tables_expose_no_mutation_seam()
	{
		// FDG L2: the process-global table is immutable. There is no installer (`Use`) and no swappable
		// backing field on either holder, so a consumer cannot replace the table out from under in-flight
		// evaluations and the test suite needs no serialised process-global phase. `Default` is a pure,
		// lazily-loaded shipped snapshot; constructed engine paths thread their own explicit snapshots.
		const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

		typeof(Catalogue).GetMethod("Use", any).Should().BeNull();
		typeof(QualificationScale).GetMethod("Use", any).Should().BeNull();
		typeof(Catalogue).GetField("installed", any).Should().BeNull();
		typeof(QualificationScale).GetField("installed", any).Should().BeNull();
	}

	[Fact]
	public void evaluation_surface_accepts_a_terminal_cancellation_token()
	{
		typeof(IEnrolmentEngine).GetMethods()
			.Where(static method => method.Name is nameof(IEnrolmentEngine.EvaluateAsync) or nameof(IEnrolmentEngine.ExplainAsync)
				or nameof(IEnrolmentEngine.AdviseAsync))
			.Should()
			.OnlyContain(static method => method.GetParameters().Last().ParameterType == typeof(CancellationToken));
	}

	// Strong all-round student sitting on the Art age gate: Art GCSE at StrongEntry (6) with a high average.
	// A non-adult clears Art's strong-entry threshold; an adult is held to top-entry (7) and cannot. Born
	// 2007-09-01, so they are 18 before that date in 2026 and 19 on/after it.
	private static StudentInput ArtAgeGatedStudent() =>
		new("S-AGE",
			new Dictionary<string, int> {
				["english_language"] = 9,
				["maths"] = 9,
				["physics"] = 9,
				["chemistry"] = 9,
				["biology"] = 9,
				["art"] = 6,
			},
			[]) { DateOfBirth = new(2007, 9, 1) };

	private static Rating ArtRating(EnrolmentResult result) =>
		result.Recommendations.Single(r => r.Subject == Subject.Art).Rating;

	private static async Task<RatingEvaluator> EvaluatorAsync(PolicyThresholds thresholds) =>
		new((await Harness.BuildFromShippedWorkflowsAsync()).Engine, thresholds, Harness.Catalogue, Harness.Scale);

	[Fact]
	public async Task per_call_asof_overrides_the_bound_construction_date()
	{
		var student = ArtAgeGatedStudent();
		var (_, rulesEngine) = await Harness.BuildFromShippedWorkflowsAsync();

		var asMinor = new DateOnly(2026, 6, 25); // age 18 → Art held to strong-entry
		var asAdult = new DateOnly(2026, 9, 2); // age 19 → Art held to top-entry

		// Engine bound to the adult date; the per-call overload must win over that binding.
		var engine = new EnrolmentEngine(rulesEngine, Harness.Thresholds, Harness.Catalogue, asAdult);

		var minorVerdict = await engine.EvaluateAsync(student, asMinor);
		var boundVerdict = await engine.EvaluateAsync(student);

		ArtRating(minorVerdict).Should().NotBe(ArtRating(boundVerdict));
		((int)ArtRating(minorVerdict)).Should().BeLessThan((int)ArtRating(boundVerdict));
	}

	[Fact]
	public async Task live_date_source_is_resolved_afresh_on_each_evaluation()
	{
		var student = ArtAgeGatedStudent();
		var (_, rulesEngine) = await Harness.BuildFromShippedWorkflowsAsync();

		var today = new DateOnly(2026, 6, 25); // age 18
		var evaluator = new RatingEvaluator(rulesEngine, Harness.Thresholds, Harness.Catalogue);
		var engine = new EnrolmentEngine(evaluator, Harness.Catalogue, () => today);

		var beforeBirthday = await engine.EvaluateAsync(student);
		today = new(2026, 9, 2); // mutate the source: age 19
		var afterBirthday = await engine.EvaluateAsync(student);

		((int)ArtRating(beforeBirthday)).Should().BeLessThan((int)ArtRating(afterBirthday));
	}

	[Fact]
	public async Task create_async_exposes_the_catalogue_it_built()
	{
		var created = await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, DataDir, Harness.AsOf);

		// The CLI validates student input against this instance instead of reloading or reading the global.
		created.Catalogue.Subjects.Should().BeEquivalentTo(CatalogueStore.LoadAndValidate(DataDir).Subjects);
	}

	[Fact]
	public async Task create_async_honours_a_data_directory_that_is_not_the_workflows_sibling()
	{
		// A library host may ship workflows and data in unrelated locations. The probe step must use the
		// thresholds from the explicit data directory, not re-derive a sibling `data/` from the workflows
		// parent (which here has no thresholds file at all).
		var workflowsDir = CopyDirectory(Harness.WorkflowsDir);
		var dataDir = CopyDirectory(DataDir);
		try {
			var created = await EnrolmentEngine.CreateAsync(workflowsDir, dataDir, Harness.AsOf);

			var expected = await (await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, DataDir, Harness.AsOf))
				.EvaluateAsync(ExampleStudent());
			var actual = await created.EvaluateAsync(ExampleStudent());
			actual.Should().BeEquivalentTo(expected);
		}
		finally {
			Directory.Delete(workflowsDir, true);
			Directory.Delete(dataDir, true);
		}
	}

	[Fact]
	public async Task create_async_sources_the_transition_matrix_from_the_data_directory()
	{
		// The DfE transition matrix is data like the catalogue and thresholds: an engine pointed at a data
		// directory whose matrix is degraded must reflect that, not silently fall back to the bundled file.
		var degradedData = CopyDirectory(DataDir);
		ZeroTransitionProbabilities(Path.Combine(
			degradedData, "dfe-transition-matrices", "gce-a-level-2019-transition-probabilities.csv"));
		try {
			var pristine = await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, DataDir, Harness.AsOf);
			var degraded = await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, degradedData, Harness.AsOf);

			var student = ExampleStudent();
			var pristineResult = await pristine.EvaluateAsync(student);
			var degradedResult = await degraded.EvaluateAsync(student);

			// Zeroed transition probabilities fail every probability-gated tier, so no subject can be green.
			pristineResult.Summary.GreenCount.Should().BeGreaterThan(0);
			degradedResult.Summary.GreenCount.Should().Be(0);
		}
		finally {
			Directory.Delete(degradedData, true);
		}
	}

	// Rewrite a transition-matrix CSV into a valid but maximally pessimistic matrix: all mass moves to U, so
	// every probability gate above U fails while each row still sums to 1 and passes structural validation.
	private static void ZeroTransitionProbabilities(string csvPath)
	{
		var lines = File.ReadAllLines(csvPath);
		var rewritten = lines.Select(static (line, index) => {
			if (index == 0) {
				return line;
			}

			var fields = line.Split(',');
			fields[5] = "1";
			for (var i = 6; i < fields.Length; i++) {
				fields[i] = "0";
			}

			return string.Join(',', fields);
		}).ToArray();
		File.WriteAllLines(csvPath, rewritten);
		DfeTransitionMatrix.Load(csvPath);
	}

	// Copy a directory tree into a fresh, isolated temp location whose parent has no sibling `data/` folder,
	// so a probe that walks to the workflows parent for thresholds fails rather than silently finding them.
	private static string CopyDirectory(string source)
	{
		var destination = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"), "payload");
		foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) {
			Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
		}

		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), true);
		}

		return destination;
	}

	[Fact]
	public async Task create_async_matches_the_hand_wired_bootstrap_for_a_real_student()
	{
		var student = ExampleStudent();
		var manualWorkflows = WorkflowStore.LoadAndValidate(Harness.WorkflowsDir, Harness.SchemaPath);
		var manualCatalogue = CatalogueStore.LoadAndValidate(DataDir);
		var manualThresholds = PolicyThresholdsStore.LoadAndValidate(DataDir);
		var manualEngine = WorkflowStore.BuildEngine(manualWorkflows);
		await WorkflowStore.ProbeCompileAsync(manualEngine, manualWorkflows, Harness.CanonicalProbe(manualThresholds));
		var manual = new EnrolmentEngine(manualEngine, manualThresholds, manualCatalogue, Harness.AsOf);

		var created = await EnrolmentEngine.CreateAsync(Harness.WorkflowsDir, DataDir, Harness.AsOf);

		var expected = await manual.EvaluateAsync(student);
		var actual = await created.EvaluateAsync(student);

		actual.Should().BeEquivalentTo(expected);
	}
}
