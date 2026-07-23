namespace EnrolmentRules.Cli;

using System.Text.Json;
using Domain;
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
///     <c>--criteria &lt;subject&gt;</c> is the odd one out: it takes no student at all, printing what the
///     rules require of anyone, narrated from the loaded workflows rather than from a separate prospectus.
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

	public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr) =>
		Run(args, stdout, stderr, WorkflowsDirectory, DataDirectory);

	public static int Run(
		IReadOnlyList<string> args,
		TextWriter stdout,
		TextWriter stderr,
		Func<string> workflowsDirectory,
		Func<string> dataDirectory) =>
		args switch {
			["--version"] or ["-v"] => RunVersion(stdout),
			["--lint-workflows"] => RunLint(null, stdout, stderr),
			["--lint-workflows", var dir] => RunLint(dir, stdout, stderr),
			// Ahead of the bare-path arm: otherwise a --criteria with no subject is read as a student file
			// and reported as an unreadable document rather than a missing argument.
			["--criteria"] => Usage(stderr),
			[var path] => RunProfile(path, stdout, stderr, dataDirectory),
			["--table", var path] => RunEvaluation(path, Output.Table, stdout, stderr, null, workflowsDirectory, dataDirectory),
			["--json", var path] => RunEvaluation(path, Output.Json, stdout, stderr, null, workflowsDirectory, dataDirectory),
			["--explain", var path] => RunEvaluation(path, Output.Explain, stdout, stderr, null, workflowsDirectory, dataDirectory),
			["--explain-text", var path] => RunEvaluation(path, Output.ExplainText, stdout, stderr, null, workflowsDirectory, dataDirectory),
			["--advise", var path] => RunEvaluation(path, Output.Advise, stdout, stderr, null, workflowsDirectory, dataDirectory),
			["--advise", "--all-gcses", var path] =>
				RunEvaluation(path, Output.Advise, stdout, stderr, true, workflowsDirectory, dataDirectory),
			["--batch", var path] => RunBatch(path, stdout, stderr, workflowsDirectory, dataDirectory),
			["--criteria", var subject] => RunCriteria(subject, stdout, stderr, workflowsDirectory, dataDirectory),
			_ => Usage(stderr),
		};

	private static int Usage(TextWriter stderr)
	{
		stderr.WriteLine("usage: enrolment [--table|--json|--explain|--explain-text|--advise] <student.json|.yaml>");
		stderr.WriteLine("       enrolment --advise [--all-gcses] <student.json|.yaml>");
		stderr.WriteLine("       enrolment --batch <students.jsonl>");
		stderr.WriteLine("       enrolment --criteria <subject>");
		stderr.WriteLine("       enrolment --lint-workflows [workflows-dir]");
		stderr.WriteLine("       enrolment --version|-v");
		return ExitUsage;
	}

	/// <summary>Prints the build stamp — version and the git commit the binary was built from.</summary>
	private static int RunVersion(TextWriter stdout)
	{
		stdout.WriteLine($"enrolment {BuildInfo.VersionWithCommit}");
		return ExitOk;
	}

	/// <summary>
	///     Static structural lint of a workflow directory (§ Reservation 1) — the shipped one by default, or
	///     <paramref name="directory" /> when given (used to lint a candidate set before shipping). Loads and
	///     schema-validates the workflows, then reports every <see cref="WorkflowLinter" /> finding one per
	///     line. Exit <see cref="ExitOk" /> when clean, <see cref="ExitLint" /> on any
	///     <see cref="LintSeverity.Error" />.
	/// </summary>
	private static int RunLint(string? directory, TextWriter stdout, TextWriter stderr)
	{
		IReadOnlyList<Workflow> workflows;
		CatalogueData catalogue;
		try {
			var workflowsDirectory = directory ?? WorkflowsDirectory();
			var loadedDataDirectory = CatalogueDirectoryForLint(workflowsDirectory);
			var scale = QualificationScaleStore.LoadAndValidate(QualificationScaleDirectoryForLint(loadedDataDirectory));
			workflows = WorkflowStore.LoadAndValidate(workflowsDirectory);
			catalogue = CatalogueStore.LoadAndValidate(loadedDataDirectory, scale);
		}
		catch (Exception ex) when (ex is WorkflowException or CatalogueException or QualificationScaleException
									   or DirectoryNotFoundException or FileNotFoundException) {
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

	private static string QualificationScaleDirectoryForLint(string catalogueDirectory) =>
		File.Exists(Path.Combine(catalogueDirectory, QualificationScaleStore.QualificationsFileName))
			? catalogueDirectory
			: DataDirectory();

	private static int RunProfile(string path, TextWriter stdout, TextWriter stderr, Func<string> dataDirectory)
	{
		try {
			var loadedDataDirectory = dataDirectory();
			var scale = QualificationScaleStore.LoadAndValidate(loadedDataDirectory);
			var catalogue = CatalogueStore.LoadAndValidate(loadedDataDirectory, scale);
			if (LoadValidStudent(path, stderr, catalogue, scale) is not StudentInput student) {
				return ExitInput;
			}

			var profile = GradePredictor.Predict(student, student.ToGcseResults(), Today, catalogue,
				DfeTransitionMatrix.LoadFromDataDirectory(loadedDataDirectory), scale);
			stdout.WriteLine(JsonSerializer.Serialize(profile, EnrolmentJsonContext.Default.StudentProfile));
			return ExitOk;
		}
		catch (Exception ex) when (ex is CatalogueException or QualificationScaleException or TransitionMatrixException
									   or DirectoryNotFoundException or FileNotFoundException) {
			stderr.WriteLine($"error: could not load enrolment rules: {ex.Message}");
			return ExitInput;
		}
	}

	/// <summary>
	///     Print one subject's criteria in plain English. Takes no student — this is what the rules
	///     <em>require</em>, narrated from the same workflow graph the engine evaluates, so it stays correct
	///     as policy is retuned without anyone maintaining a second prospectus.
	/// </summary>
	private static int RunCriteria(
		string subject,
		TextWriter stdout,
		TextWriter stderr,
		Func<string> workflowsDirectory,
		Func<string> dataDirectory)
	{
		if (BuildEngine(stderr, workflowsDirectory, dataDirectory) is not EnrolmentEngine engine) {
			return ExitInput;
		}

		if (!Subject.TryParse(subject, out var parsed) || !engine.Catalogue.Subjects.Contains(parsed)) {
			stderr.WriteLine($"error: '{subject}' is not a subject offered by this college.");
			stderr.WriteLine($"       available: {string.Join(", ", engine.Catalogue.Subjects.Select(EnumNames.NameOf))}");
			return ExitInput;
		}

		CriteriaRenderer.Render(engine.Describe(parsed), stdout);
		return ExitOk;
	}

	// considerUnsatGcses is null in normal use so --advise honours the loaded thresholds default; the
	// --all-gcses flag passes true to force the diagnostic search over every known GCSE for this run only.
	private static int RunEvaluation(
		string path,
		Output output,
		TextWriter stdout,
		TextWriter stderr,
		bool? considerUnsatGcses,
		Func<string> workflowsDirectory,
		Func<string> dataDirectory)
	{
		if (BuildEngine(stderr, workflowsDirectory, dataDirectory) is not EnrolmentEngine engine) {
			return ExitInput;
		}

		if (Load(path, stderr) is not StudentDocument document) {
			return ExitInput;
		}

		var useExplanation = output is Output.Explain or Output.ExplainText;
		var useAdvice = output == Output.Advise;
		if (useExplanation) {
			var outcome = engine.TryExplain(document.Student);
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
			var outcome = considerUnsatGcses.HasValue
				? engine.TryAdvise(document.Student, considerUnsatGcses.Value)
				: engine.TryAdvise(document.Student);
			if (!outcome.Validation.IsValid) {
				WriteValidationErrors(stderr, outcome.Validation);
				return ExitInput;
			}

			stdout.WriteLine(JsonSerializer.Serialize(outcome.Value!, EnrolmentJsonContext.Default.AdviceResult));
			return outcome.Value!.Eligible ? ExitOk : ExitIneligible;
		}

		var evaluation = engine.TryEvaluate(document.Student);
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
	private static int RunBatch(
		string path,
		TextWriter stdout,
		TextWriter stderr,
		Func<string> workflowsDirectory,
		Func<string> dataDirectory)
	{
		string[] lines;
		try {
			lines = File.ReadAllLines(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			stderr.WriteLine($"error: could not read batch file '{path}': {ex.Message}");
			return ExitInput;
		}

		if (BuildEngine(stderr, workflowsDirectory, dataDirectory) is not EnrolmentEngine engine) {
			return ExitInput;
		}

		var students = lines
			.Where(static line => !string.IsNullOrWhiteSpace(line))
			.ToArray();
		var outcomes = new BatchOutcome[students.Length];

		_ = Parallel.For(
			0,
			students.Length,
			new() { MaxDegreeOfParallelism = Environment.ProcessorCount },
			index => outcomes[index] = EvaluateLine(students[index], engine));

		foreach (var outcome in outcomes) {
			stdout.WriteLine(JsonSerializer.Serialize(outcome, BatchJsonContext.Default.BatchOutcome));
		}

		return ExitOk;
	}

	private static BatchOutcome EvaluateLine(string line, EnrolmentEngine engine)
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

		var outcome = engine.TryEvaluate(document.Student);
		if (!outcome.Validation.IsValid) {
			return new(document.Student?.Id ?? "?", null, string.Join("; ", outcome.Validation.Errors));
		}

		return new(document.Student.Id, outcome.Value, null);
	}

	/// <summary>Build the façade over the shipped workflows, reporting a load failure as an input error.</summary>
	private static EnrolmentEngine? BuildEngine(
		TextWriter stderr,
		Func<string> workflowsDirectory,
		Func<string> dataDirectory)
	{
		try {
			return EnrolmentEngine.Create(workflowsDirectory(), dataDirectory(), Today);
		}
		catch (Exception ex) when (ex is WorkflowException or CatalogueException or QualificationScaleException
									   or PolicyThresholdsException or TransitionMatrixException
									   or DirectoryNotFoundException or FileNotFoundException) {
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
		if (Load(path, stderr) is not StudentDocument document) {
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
	// formats share one validation path downstream. (--batch stays JSONL-only: see RunBatch.)
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
