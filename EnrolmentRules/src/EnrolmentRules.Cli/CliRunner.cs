namespace EnrolmentRules.Cli;

using System.Text.Json;
using Domain;
using Engine;
using Prediction;
using RulesEngine.Models;

/// <summary>
///     The in-process CLI runner, driven directly by tests (the <see cref="Program" /> entry point is a
///     thin shim over it). Single-student modes accept a JSON or YAML document (extension-dispatched);
///     <c>--batch</c> stays JSONL-only. Modes: bare <c>&lt;student.json&gt;</c> prints the prediction profile (Phase 1);
///     <c>--table</c> the coloured traffic-light table; <c>--json</c> the raw <see cref="EnrolmentResult" />;
///     <c>--explain</c> the <see cref="ExplainedResult" /> with provenance (Phase 7);
///     <c>--explain-text</c> the same explanation as Markdown prose; <c>--batch</c> a JSONL
///     stream evaluated in parallel over one shared, stateless engine. Every mode validates the input
///     document first (Phase 8) so a bad grade fails fast instead of becoming a silent red.
/// </summary>
public static class CliRunner
{
	/// <summary>Process exit codes (§ CLI contract).</summary>
	public const int ExitOk = 0;

	public const int ExitUsage = 2;
	public const int ExitInput = 3;

	/// <summary>An ineligible student in a single-student evaluation mode (<c>--json/--explain/--table</c>).</summary>
	public const int ExitIneligible = 4;

	/// <summary>At least one <see cref="LintSeverity.Error" /> finding from <c>--lint-workflows</c>.</summary>
	public const int ExitLint = 5;

	/// <summary>
	///     The reference ("as-of") date age-gated rules derive each student's age against. The CLI uses the
	///     current local date — the deterministic core takes this explicitly, so the wall clock is read only
	///     here at the process edge, never inside the engine.
	/// </summary>
	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

	public static Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr) =>
		args switch {
			["--lint-workflows"] => RunLintAsync(null, stdout, stderr),
			["--lint-workflows", var dir] => RunLintAsync(dir, stdout, stderr),
			[var path] => Task.FromResult(RunProfile(path, stdout, stderr)),
			["--table", var path] => RunEvaluationAsync(path, Output.Table, stdout, stderr),
			["--json", var path] => RunEvaluationAsync(path, Output.Json, stdout, stderr),
			["--explain", var path] => RunEvaluationAsync(path, Output.Explain, stdout, stderr),
			["--explain-text", var path] => RunEvaluationAsync(path, Output.ExplainText, stdout, stderr),
			["--advise", var path] => RunEvaluationAsync(path, Output.Advise, stdout, stderr),
			["--advise", "--all-gcses", var path] =>
				RunEvaluationAsync(path, Output.Advise, stdout, stderr, true),
			["--batch", var path] => RunBatchAsync(path, stdout, stderr),
			_ => Task.FromResult(Usage(stderr)),
		};

	private static int Usage(TextWriter stderr)
	{
		stderr.WriteLine("usage: enrolment [--table|--json|--explain|--explain-text|--advise] <student.json|.yaml>");
		stderr.WriteLine("       enrolment --advise [--all-gcses] <student.json|.yaml>");
		stderr.WriteLine("       enrolment --batch <students.jsonl>");
		stderr.WriteLine("       enrolment --lint-workflows [workflows-dir]");
		return ExitUsage;
	}

	/// <summary>
	///     Static structural lint of a workflow directory (§ Reservation 1) — the shipped one by default, or
	///     <paramref name="directory" /> when given (used to lint a candidate set before shipping). Loads and
	///     schema-validates the workflows, then reports every <see cref="WorkflowLinter" /> finding one per
	///     line. Exit <see cref="ExitOk" /> when clean, <see cref="ExitLint" /> on any
	///     <see cref="LintSeverity.Error" />.
	/// </summary>
	private static async Task<int> RunLintAsync(string? directory, TextWriter stdout, TextWriter stderr)
	{
		IReadOnlyList<Workflow> workflows;
		CatalogueData catalogue;
		try {
			var workflowsDirectory = directory ?? WorkflowsDirectory();
			workflows = WorkflowStore.LoadAndValidate(workflowsDirectory);
			catalogue = CatalogueStore.LoadAndValidate(CatalogueDirectoryForLint(workflowsDirectory));
		}
		catch (Exception ex) when (ex is WorkflowException or CatalogueException or DirectoryNotFoundException) {
			stderr.WriteLine($"error: could not load enrolment workflows: {ex.Message}");
			return ExitInput;
		}

		var findings = WorkflowLinter.Lint(workflows, catalogue);
		foreach (var finding in findings) {
			stdout.WriteLine($"{finding.Severity}: {finding.Workflow}/{finding.Rule ?? "-"}: {finding.Message}");
		}

		return findings.Any(static finding => finding.Severity == LintSeverity.Error) ? ExitLint : ExitOk;
	}

	private static string CatalogueDirectoryForLint(string workflowsDirectory)
	{
		var sibling = Path.Combine(Directory.GetParent(Path.GetFullPath(workflowsDirectory))?.FullName ?? string.Empty, "data");
		return Directory.Exists(sibling) ? sibling : DataDirectory();
	}

	private static int RunProfile(string path, TextWriter stdout, TextWriter stderr)
	{
		var dataDirectory = DataDirectory();
		var scale = QualificationScaleStore.LoadAndValidate(dataDirectory);
		var catalogue = CatalogueStore.LoadAndValidate(dataDirectory, scale);
		if (LoadValidStudent(path, stderr, catalogue, scale) is not { } student) {
			return ExitInput;
		}

		var profile = GradePredictor.Predict(student, student.ToGcseResults(), Today, catalogue, scale);
		stdout.WriteLine(JsonSerializer.Serialize(profile, EnrolmentJsonContext.Default.StudentProfile));
		return ExitOk;
	}

	// considerUnsatGcses is null in normal use so --advise honours the loaded thresholds default; the
	// --all-gcses flag passes true to force the diagnostic search over every known GCSE for this run only.
	private static async Task<int> RunEvaluationAsync(
		string path, Output output, TextWriter stdout, TextWriter stderr, bool? considerUnsatGcses = null)
	{
		if (await BuildEngineAsync(stderr) is not { } engine) {
			return ExitInput;
		}

		if (Load(path, stderr) is not { } document) {
			return ExitInput;
		}

		var useExplanation = output is Output.Explain or Output.ExplainText;
		var useAdvice = output == Output.Advise;
		if (useExplanation) {
			var outcome = await engine.TryExplainAsync(document.Student);
			if (!outcome.Validation.IsValid) {
				WriteValidationErrors(stderr, outcome.Validation);
				return ExitInput;
			}

			switch (output) {
				case Output.Explain:
					stdout.WriteLine(JsonSerializer.Serialize(outcome.Value!, EnrolmentJsonContext.Default.ExplainedResult));
					break;
				case Output.ExplainText:
					ExplanationRenderer.Render(outcome.Value!, stdout);
					break;
			}

			return outcome.Value!.Eligible ? ExitOk : ExitIneligible;
		}

		if (useAdvice) {
			var outcome = considerUnsatGcses is { } flag
				? await engine.TryAdviseAsync(document.Student, flag)
				: await engine.TryAdviseAsync(document.Student);
			if (!outcome.Validation.IsValid) {
				WriteValidationErrors(stderr, outcome.Validation);
				return ExitInput;
			}

			stdout.WriteLine(JsonSerializer.Serialize(outcome.Value!, EnrolmentJsonContext.Default.AdviceResult));
			return outcome.Value!.Eligible ? ExitOk : ExitIneligible;
		}

		var evaluation = await engine.TryEvaluateAsync(document.Student);
		if (!evaluation.Validation.IsValid) {
			WriteValidationErrors(stderr, evaluation.Validation);
			return ExitInput;
		}

		var result = evaluation.Value!;
		switch (output) {
			case Output.Json:
				stdout.WriteLine(JsonSerializer.Serialize(result, EnrolmentJsonContext.Default.EnrolmentResult));
				break;
			case Output.Table:
			default:
				TableRenderer.Render(result, stdout);
				break;
		}

		return result.Eligible ? ExitOk : ExitIneligible;
	}

	/// <summary>
	///     Evaluate a JSONL stream over a single shared engine: each non-blank line is one student, evaluated
	///     in parallel (the engine is stateless, so there is nothing to leak between students), with input
	///     order preserved in the output. A parse or validation failure on one line is isolated to that
	///     line's <see cref="BatchOutcome" /> rather than aborting the whole run.
	/// </summary>
	private static async Task<int> RunBatchAsync(string path, TextWriter stdout, TextWriter stderr)
	{
		string[] lines;
		try {
			lines = File.ReadAllLines(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			stderr.WriteLine($"error: could not read batch file '{path}': {ex.Message}");
			return ExitInput;
		}

		if (await BuildEngineAsync(stderr) is not { } engine) {
			return ExitInput;
		}

		var students = lines
			.Where(static line => !string.IsNullOrWhiteSpace(line))
			.ToArray();
		var outcomes = new BatchOutcome[students.Length];

		await Parallel.ForAsync(
			0,
			students.Length,
			new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
			async (index, _) => outcomes[index] = await EvaluateLineAsync(students[index], engine));

		foreach (var outcome in outcomes) {
			stdout.WriteLine(JsonSerializer.Serialize(outcome, BatchJsonContext.Default.BatchOutcome));
		}

		return ExitOk;
	}

	private static async Task<BatchOutcome> EvaluateLineAsync(string line, EnrolmentEngine engine)
	{
		StudentDocument? document;
		try {
			document = JsonSerializer.Deserialize(line, EnrolmentJsonContext.Default.StudentDocument);
		}
		catch (JsonException ex) {
			return new("?", null, $"could not parse student JSON: {ex.Message}");
		}

		if (document is null) {
			return new("?", null, "student document was empty or null");
		}

		var outcome = await engine.TryEvaluateAsync(document.Student);
		if (!outcome.Validation.IsValid) {
			return new(document.Student?.Id ?? "?", null, string.Join("; ", outcome.Validation.Errors));
		}

		return new(document.Student.Id, outcome.Value, null);
	}

	/// <summary>Build the façade over the shipped workflows, reporting a load failure as an input error.</summary>
	private static async Task<EnrolmentEngine?> BuildEngineAsync(TextWriter stderr)
	{
		try {
			return await EnrolmentEngine.CreateAsync(WorkflowsDirectory(), DataDirectory(), Today);
		}
		catch (Exception ex) when (ex is WorkflowException or CatalogueException or QualificationScaleException
									   or PolicyThresholdsException or DirectoryNotFoundException or FileNotFoundException) {
			stderr.WriteLine($"error: could not load enrolment rules: {ex.Message}");
			return null;
		}
	}

	private static void WriteValidationErrors(TextWriter stderr, ValidationOutcome validation)
	{
		foreach (var error in validation.Errors) {
			stderr.WriteLine($"error: {error}");
		}
	}

	/// <summary>
	///     Load a single student document and validate it (§ Phase 8 boundary guard). A read/parse failure or
	///     a validation problem is reported to <paramref name="stderr" /> and yields <c>null</c> (an input
	///     error), so the caller never evaluates a malformed document.
	/// </summary>
	private static StudentInput? LoadValidStudent(string path, TextWriter stderr, CatalogueData catalogue, QualificationScale scale)
	{
		if (Load(path, stderr) is not { } document) {
			return null;
		}

		var errors = StudentValidator.Validate(document.Student, catalogue, scale);
		if (errors.Count == 0) {
			return document.Student;
		}

		foreach (var error in errors) {
			stderr.WriteLine($"error: {error}");
		}

		return null;
	}

	// A single-student document may be JSON or YAML; the extension selects the parser. YAML is normalized
	// to the same JsonNode shape and deserialized through the same source-generated contract, so both
	// formats share one validation path downstream. (--batch stays JSONL-only: see RunBatchAsync.)
	private static StudentDocument? Load(string path, TextWriter stderr)
	{
		try {
			var document = Path.GetExtension(path) is ".yaml" or ".yml"
				? YamlConverter.ToJsonNode(File.ReadAllText(path)).Deserialize(EnrolmentJsonContext.Default.StudentDocument)
				: JsonSerializer.Deserialize(File.ReadAllText(path), EnrolmentJsonContext.Default.StudentDocument);
			if (document is null) {
				stderr.WriteLine($"error: student document '{path}' was empty or null");
			}

			return document;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException) {
			stderr.WriteLine($"error: could not read student document '{path}': {ex.Message}");
			return null;
		}
	}

	/// <summary>
	///     Locate the shipped <c>workflows/</c> directory by walking up from the executable to the solution
	///     root.
	/// </summary>
	private static string WorkflowsDirectory()
	{
		var bundled = Path.Combine(AppContext.BaseDirectory, "workflows");
		if (Directory.Exists(bundled)) {
			return bundled;
		}

		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			var candidate = Path.Combine(dir.FullName, "workflows");
			if (File.Exists(Path.Combine(dir.FullName, "EnrolmentRules.slnx")) && Directory.Exists(candidate)) {
				return candidate;
			}

			dir = dir.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate the 'workflows' directory from " + AppContext.BaseDirectory + ".");
	}

	/// <summary>
	///     Locate the shipped <c>data/</c> directory (carrying the catalogue and DfE matrix) the same way as
	///     <see cref="WorkflowsDirectory" />: prefer the copy beside the executable, else walk up to the root.
	/// </summary>
	private static string DataDirectory()
	{
		var bundled = Path.Combine(AppContext.BaseDirectory, "data");
		if (Directory.Exists(bundled)) {
			return bundled;
		}

		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			var candidate = Path.Combine(dir.FullName, "data");
			if (File.Exists(Path.Combine(dir.FullName, "EnrolmentRules.slnx")) && Directory.Exists(candidate)) {
				return candidate;
			}

			dir = dir.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate the 'data' directory from " + AppContext.BaseDirectory + ".");
	}

	private enum Output
	{
		Table,
		Json,
		Explain,
		ExplainText,
		Advise,
	}
}
