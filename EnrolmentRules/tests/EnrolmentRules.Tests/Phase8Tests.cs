namespace EnrolmentRules.Tests;

using System.Text.Json;
using Cli;
using Domain;
using FluentAssertions;

/// <summary>
///     Phase 8 — CLI polish, input validation, and parallel batch evaluation. Input validation is the
///     boundary guard RulesEngine does not give the input <em>document</em> (a bad grade must fail fast,
///     not become a silent red). <c>--batch</c> is the explicit demonstration that stateless evaluation
///     parallelises for free over one shared engine: one well-formed line per student, input order
///     preserved, no cross-student leakage. The ineligible exit code (4) is asserted for the
///     single-student evaluation modes.
/// </summary>
public sealed class Phase8Tests
{
	// A valid date of birth so its presence never confounds the other validation assertions.
	private const string DobText = "2009-09-01";
	private static readonly DateOnly ValidDob = new(2009, 9, 1);

	// A minimal eligible student: English + Maths passes and exactly MinPasses GCSEs at grade >= 4.
	private static string EligibleLine(string id) =>
		StudentLine(id, """{"english_language":6,"maths":6,"physics":6,"chemistry":6,"biology":6}""");

	private static string StudentLine(string id, string gcsesJson, string hobbiesJson = "[]") =>
		"{\"student\":{\"id\":\"" + id + "\",\"gcses\":" + gcsesJson + ",\"hobbies\":" + hobbiesJson + ",\"date_of_birth\":\"" + DobText + "\"}}";

	private static string WriteTemp(string contents, string extension)
	{
		var path = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N") + extension);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
		return path;
	}

	// ---- input validation (pure) -------------------------------------------------------------

	[Fact]
	public void a_well_formed_student_has_no_validation_errors()
	{
		var student = new StudentInput(
			"S-OK",
			new Dictionary<string, int> { ["maths"] = 6, ["english_language"] = 5 },
			["plays_piano"]) { DateOfBirth = ValidDob };

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale).Should().BeEmpty();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(10)]
	[InlineData(-3)]
	public void a_grade_outside_the_one_to_nine_scale_is_rejected(int grade)
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = grade }, []) { DateOfBirth = ValidDob };

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale)
			.Should().ContainSingle()
			.Which.Should().Contain("maths").And.Contain("out of range");
	}

	[Fact]
	public void grades_at_the_scale_boundaries_are_accepted()
	{
		var student = new StudentInput(
			"S-EDGE",
			new Dictionary<string, int> { ["maths"] = Thresholds.MinGcseGrade, ["art"] = Thresholds.MaxGcseGrade },
			[]) { DateOfBirth = ValidDob };

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale).Should().BeEmpty();
	}

	[Fact]
	public void an_unknown_gcse_subject_is_rejected()
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["underwater_basketweaving"] = 6 }, []) { DateOfBirth = ValidDob };

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale)
			.Should().ContainSingle()
			.Which.Should().Contain("underwater_basketweaving");
	}

	[Fact]
	public void a_blank_hobby_tag_is_rejected()
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = 6 }, ["plays_piano", "   "]) { DateOfBirth = ValidDob };

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale)
			.Should().ContainSingle()
			.Which.Should().Contain("hobby");
	}

	[Fact]
	public void duplicate_chosen_a_levels_are_rejected()
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = 6 }, []) {
			ChosenALevels = [Subject.French, Subject.German, Subject.French],
			DateOfBirth = ValidDob,
		};

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale)
			.Should().ContainSingle()
			.Which.Should().Contain("chosen_a_levels").And.Contain("duplicate");
	}

	[Fact]
	public void an_unknown_chosen_a_level_value_is_rejected()
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = 6 }, []) {
			ChosenALevels = [new("philosophy")],
			DateOfBirth = ValidDob,
		};

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale)
			.Should().ContainSingle()
			.Which.Should().Contain("chosen_a_levels").And.Contain("philosophy");
	}

	[Fact]
	public void a_missing_date_of_birth_is_rejected()
	{
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = 6 }, ["plays_piano"]);

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale)
			.Should().ContainSingle()
			.Which.Should().Contain("date_of_birth").And.Contain("required");
	}

	// ---- CLI validation gating ---------------------------------------------------------------

	[Fact]
	public async Task cli_rejects_an_out_of_range_grade_with_an_input_error_not_a_silent_rating()
	{
		var path = WriteTemp(StudentLine("S-BAD", """{"maths":10}"""), ".json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitInput);
		stdout.ToString().Should().BeEmpty();
		stderr.ToString().Should().Contain("out of range");
	}

	[Fact]
	public async Task cli_rejects_an_unknown_subject_with_an_input_error()
	{
		var path = WriteTemp(StudentLine("S-BAD", """{"quidditch":6}"""), ".json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--table", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitInput);
		stderr.ToString().Should().Contain("quidditch");
	}

	[Fact]
	public async Task cli_rejects_missing_required_student_members_with_an_input_error()
	{
		var path = WriteTemp("""{"student":{"id":"S-BAD","gcses":{"maths":6}}}""", ".json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitInput);
		stdout.ToString().Should().BeEmpty();
		stderr.ToString().Should().Contain("hobbies");
	}

	[Fact]
	public async Task cli_rejects_an_unknown_chosen_a_level_value_with_an_input_error()
	{
		var path = WriteTemp("""{"student":{"id":"S-BAD","gcses":{"maths":6},"hobbies":[],"chosen_a_levels":["philosophy"]}}""", ".json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitInput);
		stdout.ToString().Should().BeEmpty();
		stderr.ToString().Should().Contain("chosen_a_levels").And.Contain("philosophy");
	}

	// ---- ineligible exit code (single-student modes) -----------------------------------------

	[Fact]
	public async Task cli_json_on_an_ineligible_student_exits_ineligible_but_still_emits_the_result()
	{
		// Only Maths present ⇒ no English pass and too few passes ⇒ ineligible.
		var path = WriteTemp(StudentLine("S-INELIGIBLE", """{"maths":6}"""), ".json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitIneligible);
		var result = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.EnrolmentResult);
		result.Should().NotBeNull();
		result!.Eligible.Should().BeFalse();
	}

	[Fact]
	public async Task cli_json_on_an_eligible_student_exits_ok()
	{
		var path = WriteTemp(EligibleLine("S-OK"), ".json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		(await CliRunner.RunAsync(["--json", path], stdout, stderr)).Should().Be(CliRunner.ExitOk);
	}

	// ---- coloured table ----------------------------------------------------------------------

	[Fact]
	public async Task cli_table_renders_every_subject_with_its_rating()
	{
		var path = Path.Combine(Harness.RepoRoot, "examples", "student.json");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--table", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		var output = stdout.ToString();
		// Plain-text capture (no terminal ⇒ colour markup is resolved away); assert the structure is present.
		foreach (var subject in Catalogue.Subjects) {
			output.Should().Contain(EnumNames.NameOf(subject));
		}

		output.Should().MatchRegex("green|amber|red");
	}

	// ---- batch -------------------------------------------------------------------------------

	[Fact]
	public async Task cli_batch_emits_one_well_formed_result_per_line_in_input_order()
	{
		var ids = new[] { "S-A", "S-B", "S-C" };
		var jsonl = string.Join('\n', ids.Select(EligibleLine));
		var path = WriteTemp(jsonl, ".jsonl");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--batch", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		var outcomes = ParseOutcomes(stdout.ToString());

		// One line per student, input order preserved.
		outcomes.Select(o => o.Id).Should().Equal(ids);
		// Each is a well-formed result (no cross-student leakage: every line carries its own verdict).
		outcomes.Should().OnlyContain(o => o.Error == null);
		outcomes.Should().OnlyContain(o => o.Result!.Recommendations.Count == Catalogue.Subjects.Count);
		outcomes.Should().OnlyContain(o => o.Result!.Eligible);
	}

	[Fact]
	public async Task cli_batch_isolates_a_bad_line_without_aborting_the_run()
	{
		// A valid student, then one with an out-of-range grade, then another valid student.
		var jsonl = string.Join('\n',
			EligibleLine("S-A"),
			StudentLine("S-BAD", """{"maths":99}"""),
			EligibleLine("S-C"));
		var path = WriteTemp(jsonl, ".jsonl");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--batch", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		var outcomes = ParseOutcomes(stdout.ToString());

		outcomes.Select(o => o.Id).Should().Equal("S-A", "S-BAD", "S-C");
		outcomes[0].Error.Should().BeNull();
		outcomes[1].Result.Should().BeNull();
		outcomes[1].Error.Should().Contain("out of range");
		outcomes[2].Error.Should().BeNull();
	}

	[Fact]
	public async Task cli_batch_isolates_a_line_with_missing_required_members()
	{
		var jsonl = string.Join('\n',
			EligibleLine("S-A"),
			"""{"student":{"id":"S-BAD","gcses":{"maths":6}}}""",
			EligibleLine("S-C"));
		var path = WriteTemp(jsonl, ".jsonl");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--batch", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		var outcomes = ParseOutcomes(stdout.ToString());

		outcomes.Select(o => o.Id).Should().Equal("S-A", "S-BAD", "S-C");
		outcomes[1].Result.Should().BeNull();
		outcomes[1].Error.Should().Contain("hobbies");
		outcomes[2].Error.Should().BeNull();
	}

	[Fact]
	public async Task cli_batch_isolates_a_line_with_an_unknown_chosen_a_level_value()
	{
		var jsonl = string.Join('\n',
			EligibleLine("S-A"),
			"""{"student":{"id":"S-BAD","gcses":{"maths":6},"hobbies":[],"chosen_a_levels":["philosophy"]}}""",
			EligibleLine("S-C"));
		var path = WriteTemp(jsonl, ".jsonl");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--batch", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		var outcomes = ParseOutcomes(stdout.ToString());

		outcomes.Select(o => o.Id).Should().Equal("S-A", "S-BAD", "S-C");
		outcomes[0].Error.Should().BeNull();
		outcomes[1].Result.Should().BeNull();
		outcomes[1].Error.Should().Contain("chosen_a_levels").And.Contain("philosophy");
		outcomes[2].Error.Should().BeNull();
	}

	[Fact]
	public async Task cli_batch_on_a_missing_file_is_an_input_error()
	{
		var missing = Path.Combine(Path.GetTempPath(), "no-such-batch-" + Guid.NewGuid().ToString("N") + ".jsonl");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		(await CliRunner.RunAsync(["--batch", missing], stdout, stderr)).Should().Be(CliRunner.ExitInput);
	}

	// ---- YAML input (single-student modes) ---------------------------------------------------

	// The same eligible student as EligibleLine, authored as YAML rather than JSON.
	private static string EligibleYaml(string id) => $"""
													  student:
													    id: {id}
													    gcses:
													      english_language: 6
													      maths: 6
													      physics: 6
													      chemistry: 6
													      biology: 6
													    hobbies: []
													    date_of_birth: "{DobText}"
													  """;

	[Fact]
	public async Task cli_json_accepts_a_yaml_student_document()
	{
		var path = WriteTemp(EligibleYaml("S-YAML"), ".yaml");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitOk);
		var result = JsonSerializer.Deserialize(stdout.ToString(), EnrolmentJsonContext.Default.EnrolmentResult);
		result.Should().NotBeNull();
		result!.Eligible.Should().BeTrue();
	}

	[Fact]
	public async Task cli_json_on_equivalent_yaml_and_json_documents_produces_identical_output()
	{
		var jsonOut = await CaptureJsonAsync(Path.Combine(Harness.RepoRoot, "examples", "student.json"));
		var yamlOut = await CaptureJsonAsync(Path.Combine(Harness.RepoRoot, "examples", "student.yaml"));

		yamlOut.Should().Be(jsonOut);
	}

	[Fact]
	public async Task cli_applies_input_validation_to_yaml_documents_too()
	{
		const string yaml = """
							student:
							  id: S-BAD
							  gcses:
							    maths: 10
							  hobbies: []
							""";
		var path = WriteTemp(yaml, ".yml");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--table", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitInput);
		stderr.ToString().Should().Contain("out of range");
	}

	[Fact]
	public async Task cli_rejects_malformed_yaml_with_an_input_error()
	{
		// Unbalanced flow mapping ⇒ a YAML parse error, surfaced as an input error rather than a crash.
		var path = WriteTemp("student: {id: S-BAD", ".yaml");
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();

		var exit = await CliRunner.RunAsync(["--json", path], stdout, stderr);

		exit.Should().Be(CliRunner.ExitInput);
		stdout.ToString().Should().BeEmpty();
	}

	private static async Task<string> CaptureJsonAsync(string path)
	{
		await using var stdout = new StringWriter();
		await using var stderr = new StringWriter();
		(await CliRunner.RunAsync(["--json", path], stdout, stderr)).Should().Be(CliRunner.ExitOk);
		return stdout.ToString();
	}

	private static IReadOnlyList<BatchOutcome> ParseOutcomes(string stdout) => [
		.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(static line => JsonSerializer.Deserialize(line, BatchJsonContext.Default.BatchOutcome)!),
	];
}
