namespace EnrolmentRules.Cli;

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Domain;
using Prediction;

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

	// The solution file pins the shipped-directory walk to the repository root when running from the source
	// tree rather than a published output.
	private const string RootMarker = "EnrolmentRules.slnx";

	private static readonly EnrolmentPolicyId DefaultPolicyId = new(PolicyDirectoryLayout.StandardPolicyId);

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
		Run(args, stdout, stderr, workflowsDirectory, dataDirectory, PoliciesDirectory);

	internal static int Run(
		IReadOnlyList<string> args,
		TextWriter stdout,
		TextWriter stderr,
		Func<string> workflowsDirectory,
		Func<string> dataDirectory,
		Func<string> policiesDirectory)
	{
		// Discovered lazily so a mode that needs no policy (--version) never touches the policies directory.
		var policies = new Lazy<IReadOnlyList<EnrolmentPolicyDefinition>>(() => PolicyDirectoryLayout.Discover(workflowsDirectory(), dataDirectory(), policiesDirectory()));
		try {
			return RunCore(args, stdout, stderr, policies);
		}
		catch (EnrolmentPolicyConfigurationException ex) {
			stderr.WriteLine($"error: could not load enrolment policies: {ex.Message}");
			return ExitInput;
		}
	}

	private static int RunCore(
		IReadOnlyList<string> args,
		TextWriter stdout,
		TextWriter stderr,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		var (remaining, policyId, policyError) = ExtractPolicyOption(args, policies);
		if (policyError is not null) {
			stderr.WriteLine(policyError);
			return ExitUsage;
		}

		return remaining switch {
			["--version"] or ["-v"] => RunVersion(stdout),
			["--lint-workflows"] => RunLint(null, policyId, stdout, stderr, policies),
			["--lint-workflows", var dir] => RunLint(dir, policyId, stdout, stderr, policies),
			// Ahead of the bare-path arm: otherwise a --criteria with no subject is read as a student file
			// and reported as an unreadable document rather than a missing argument.
			["--criteria"] => Usage(stderr, policies),
			[var path] => RunProfile(path, stdout, stderr, policyId, policies),
			["--table", var path] =>
				RunEvaluation(path, Output.Table, stdout, stderr, null, policyId, policies),
			["--json", var path] =>
				RunEvaluation(path, Output.Json, stdout, stderr, null, policyId, policies),
			["--explain", var path] =>
				RunEvaluation(path, Output.Explain, stdout, stderr, null, policyId, policies),
			["--explain-text", var path] =>
				RunEvaluation(path, Output.ExplainText, stdout, stderr, null, policyId, policies),
			["--advise", var path] =>
				RunEvaluation(path, Output.Advise, stdout, stderr, null, policyId, policies),
			["--advise", "--all-gcses", var path] =>
				RunEvaluation(path, Output.Advise, stdout, stderr, true, policyId, policies),
			["--batch", var path] => RunBatch(path, stdout, stderr, policyId, policies),
			["--criteria", var subject] => RunCriteria(subject, stdout, stderr, policyId, policies),
			_ => Usage(stderr, policies),
		};
	}

	/// <summary>
	///     Pull <c>--policy &lt;id&gt;</c> out of <paramref name="args" /> from wherever it appears, so it
	///     reads as a global option accepted before every mode without multiplying the dispatch arms above
	///     for every possible ordering. Returns the remaining args (policy option removed), the resolved
	///     identifier (the registry default when omitted), and a usage-error message when the option is
	///     malformed, repeated, or names an identifier the discovered policy set does not carry.
	/// </summary>
	private static (IReadOnlyList<string> Remaining, EnrolmentPolicyId PolicyId, string? Error) ExtractPolicyOption(
		IReadOnlyList<string> args, Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		string? requested = null;
		var remaining = new List<string>(args.Count);
		for (var i = 0; i < args.Count; ++i) {
			if (args[i] != "--policy") {
				remaining.Add(args[i]);
				continue;
			}

			if (i + 1 >= args.Count) {
				return (remaining, DefaultPolicyId, "error: --policy requires a value");
			}

			if (requested is not null) {
				return (remaining, DefaultPolicyId, "error: --policy may only be specified once");
			}

			requested = args[++i];
		}

		if (requested is null) {
			return (remaining, DefaultPolicyId, null);
		}

		if (!EnrolmentPolicyId.TryParse(requested, out var parsed)
			|| policies.Value.All(policy => policy.Id != parsed)) {
			return (remaining, DefaultPolicyId, $"error: unknown policy '{requested}'. available: {PolicyIdList(policies)}");
		}

		return (remaining, parsed, null);
	}

	private static string PolicyIdList(Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies) =>
		string.Join(", ", policies.Value.Select(static policy => policy.Id.Value));

	private static int Usage(TextWriter stderr, Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		stderr.WriteLine("usage: enrolment [--policy <id>] [--table|--json|--explain|--explain-text|--advise] <student.json|.yaml>");
		stderr.WriteLine("       enrolment [--policy <id>] --advise [--all-gcses] <student.json|.yaml>");
		stderr.WriteLine("       enrolment [--policy <id>] --batch <students.jsonl>");
		stderr.WriteLine("       enrolment [--policy <id>] --criteria <subject>");
		stderr.WriteLine("       enrolment [--policy <id>] --lint-workflows [workflows-dir]");
		stderr.WriteLine("       enrolment --version|-v");
		stderr.WriteLine($"       <id> is one of: {PolicyIdList(policies)} (default: {DefaultPolicyId})");
		return ExitUsage;
	}

	/// <summary>Prints the build stamp — version and the git commit the binary was built from.</summary>
	private static int RunVersion(TextWriter stdout)
	{
		stdout.WriteLine($"enrolment {BuildInfo.VersionWithCommit}");
		return ExitOk;
	}

	/// <summary>
	///     Static structural lint (§ Reservation 1) of either an explicit <paramref name="directory" />
	///     (used to lint a candidate set before shipping — never policy-aware, since it names its own data
	///     directory too) or the <em>complete</em> selected policy — its own workflows/catalogue overlaid
	///     on the shared schemas/qualifications, exactly as the engine would load it, not just the
	///     directory with a Standard catalogue fallback. Loads and schema-validates the workflows, then
	///     reports every <see cref="WorkflowLinter" /> finding one per line. Exit <see cref="ExitOk" /> when
	///     clean, <see cref="ExitLint" /> on any <see cref="LintSeverity.Error" />.
	/// </summary>
	private static int RunLint(
		string? directory,
		EnrolmentPolicyId policyId,
		TextWriter stdout,
		TextWriter stderr,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		IReadOnlyList<LintFinding> findings;
		try {
			if (directory is not null) {
				var loadedDataDirectory = CatalogueDirectoryForLint(directory);
				var scale = QualificationScaleStore.LoadAndValidate(QualificationScaleDirectoryForLint(loadedDataDirectory));
				var catalogue = CatalogueStore.LoadAndValidate(loadedDataDirectory, scale);
				var gcses = GcseSubjectsStore.LoadAndValidate(GcseSubjectsDirectoryForLint(loadedDataDirectory));
				findings = WorkflowLinter.Lint(directory, catalogue, gcses: gcses);
			} else {
				findings = LoadForLint(ResolveSource(policyId, policies));
			}
		}
		catch (Exception ex) when (ex is WorkflowException or CatalogueException or QualificationScaleException
									   or GcseSubjectsException or DirectoryNotFoundException or FileNotFoundException) {
			stderr.WriteLine($"error: could not load enrolment workflows: {ex.Message}");
			return ExitInput;
		}

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

	private static string GcseSubjectsDirectoryForLint(string catalogueDirectory) =>
		File.Exists(Path.Combine(catalogueDirectory, GcseSubjectsStore.GcseSubjectsFileName))
			? catalogueDirectory
			: DataDirectory();

	/// <summary>Lint a selected policy's workflows against its catalogue, loaded through its complete <see cref="IEnrolmentDataSource" />.</summary>
	private static IReadOnlyList<LintFinding> LoadForLint(IEnrolmentDataSource source)
	{
		using var qualifications = source.OpenQualifications();
		using var qualificationsSchema = source.OpenQualificationsSchema();
		var scale = QualificationScaleStore.LoadAndValidate(qualifications, qualificationsSchema);

		using var catalogueStream = source.OpenCatalogue();
		using var catalogueSchemaStream = source.OpenCatalogueSchema();
		var catalogue = CatalogueStore.LoadAndValidate(catalogueStream, catalogueSchemaStream, scale);

		using var gcseSubjects = source.OpenGcseSubjects();
		using var gcseSubjectsSchema = source.OpenGcseSubjectsSchema();
		var gcses = GcseSubjectsStore.LoadAndValidate(gcseSubjects, gcseSubjectsSchema);

		var workflowFiles = source.OpenWorkflows();
		try {
			using var workflowSchemaStream = source.OpenWorkflowSchema();
			return WorkflowLinter.Lint(workflowFiles, workflowSchemaStream, catalogue, gcses);
		}
		finally {
			foreach (var workflow in workflowFiles) {
				workflow.Dispose();
			}
		}
	}

	/// <summary>
	///     The stream-backed data source for <paramref name="policyId" />: the shipped Standard directories
	///     directly, or an Elite-style auxiliary policy's own <c>policies/&lt;id&gt;/</c> workflows/catalogue/
	///     thresholds layered over the shared Standard schemas/qualifications/matrix via
	///     <see cref="OverlayEnrolmentDataSource" />.
	/// </summary>
	private static IEnrolmentDataSource ResolveSource(
		EnrolmentPolicyId policyId,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies) =>
		policies.Value.Single(definition => definition.Id == policyId).Source;

	private static int RunProfile(
		string path,
		TextWriter stdout,
		TextWriter stderr,
		EnrolmentPolicyId policyId,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		try {
			var source = ResolveSource(policyId, policies);

			using var qualifications = source.OpenQualifications();
			using var qualificationsSchema = source.OpenQualificationsSchema();
			var scale = QualificationScaleStore.LoadAndValidate(qualifications, qualificationsSchema);

			using var gcseSubjects = source.OpenGcseSubjects();
			using var gcseSubjectsSchema = source.OpenGcseSubjectsSchema();
			var gcses = GcseSubjectsStore.LoadAndValidate(gcseSubjects, gcseSubjectsSchema);

			using var catalogueStream = source.OpenCatalogue();
			using var catalogueSchemaStream = source.OpenCatalogueSchema();
			var catalogue = CatalogueStore.LoadAndValidate(catalogueStream, catalogueSchemaStream, scale);

			if (LoadValidStudent(path, stderr, catalogue, scale, gcses) is not StudentInput student) {
				return ExitInput;
			}

			using var matrixStream = source.OpenTransitionMatrix();
			var matrix = DfeTransitionMatrix.Load(matrixStream);
			matrix.ValidateCoverage(catalogue);
			var profile = GradePredictor.Predict(student, student.ToGcseResults(), Today, catalogue, matrix, scale);
			stdout.WriteLine(JsonSerializer.Serialize(profile, EnrolmentJsonContext.Default.StudentProfile));
			return ExitOk;
		}
		catch (Exception ex) when (ex is CatalogueException or QualificationScaleException or GcseSubjectsException or TransitionMatrixException
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
		EnrolmentPolicyId policyId,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		if (BuildEngine(stderr, policyId, policies) is not EnrolmentEngine engine) {
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
		EnrolmentPolicyId policyId,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		if (BuildEngine(stderr, policyId, policies) is not EnrolmentEngine engine) {
			return ExitInput;
		}

		if (Load(path, stderr) is not StudentDocument document) {
			return ExitInput;
		}

		var useExplanation = output is Output.Explain or Output.ExplainText;
		var useAdvice = output == Output.Advise;
		if (useExplanation) {
			var outcome = engine.ExplainValidated(document.Student);
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
			var outcome = considerUnsatGcses switch {
				true => engine.AdviseValidated(document.Student, UnsatGcseAdvice.IncludeUnsat),
				false => engine.AdviseValidated(document.Student, UnsatGcseAdvice.HeldOnly),
				null => engine.AdviseValidated(document.Student),
			};
			if (!outcome.Validation.IsValid) {
				WriteValidationErrors(stderr, outcome.Validation);
				return ExitInput;
			}

			stdout.WriteLine(JsonSerializer.Serialize(outcome.Value!, EnrolmentJsonContext.Default.AdviceResult));
			return outcome.Value!.Eligible ? ExitOk : ExitIneligible;
		}

		var evaluation = engine.EvaluateValidated(document.Student);
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
		EnrolmentPolicyId policyId,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		StreamReader? reader = null;
		try {
			reader = new(path);
			if (BuildEngine(stderr, policyId, policies) is not EnrolmentEngine engine) {
				return ExitInput;
			}

			EvaluateBatch(reader, stdout, engine);
			return ExitOk;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			stderr.WriteLine($"error: could not read batch file '{path}': {ex.Message}");
			return ExitInput;
		}
		catch (AggregateException aggregate) {
			// Parallel.Invoke wraps a worker failure. A closed output stream (stdout piped to `head`) surfaces
			// here as an IOException; treat it as an I/O input error, quietly. Anything else is a real bug —
			// rethrow it with its own type and stack so it crashes loudly rather than as a masked ExitInput.
			var inner = aggregate.Flatten().InnerExceptions;
			if (inner.FirstOrDefault(static e => e is IOException or UnauthorizedAccessException) is Exception io) {
				stderr.WriteLine($"error: could not write batch output: {io.Message}");
				return ExitInput;
			}

			var meaningful = inner.FirstOrDefault(static e => e is not OperationCanceledException) ?? inner[0];
			ExceptionDispatchInfo.Throw(meaningful);
			throw; // unreachable: ExceptionDispatchInfo.Throw does not return.
		}
		finally {
			reader?.Dispose();
		}
	}

	/// <summary>
	///     The streaming core of <c>--batch</c>: reads <paramref name="input" /> one line at a time (peak
	///     input memory is one line, not the whole file), evaluates non-blank lines with bounded parallelism
	///     over the shared <paramref name="engine" />, and writes each <see cref="BatchOutcome" /> to
	///     <paramref name="output" /> as soon as it and every earlier one are ready — so output starts before
	///     the input is exhausted rather than after, and peak output memory is the small reorder buffer of
	///     out-of-order-completed results, not the whole result set. A public reader/writer seam (not just a
	///     file path) so the streaming behaviour is directly testable and reusable over any text stream.
	/// </summary>
	public static void EvaluateBatch(TextReader input, TextWriter output, EnrolmentEngine engine)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(engine);

		EvaluateBatch(input, output, line => EvaluateLine(line, engine), Math.Max(1, Environment.ProcessorCount));
	}

	internal static void EvaluateBatch(
		TextReader input,
		TextWriter output,
		Func<string, BatchOutcome> evaluateLine,
		int maxConcurrency)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(evaluateLine);
		ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

		var writeLock = new Lock();
		var pending = new Dictionary<int, BatchOutcome>();
		var nextToWrite = 0;
		using var cancellation = new CancellationTokenSource();
		using var availableWindowSlots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
		using var work = new BlockingCollection<(int Index, string Line)>(maxConcurrency);

		void EmitInOrder(int index, BatchOutcome outcome)
		{
			var written = 0;
			using (writeLock.EnterScope()) {
				pending[index] = outcome;
				while (pending.Remove(nextToWrite, out var ready)) {
					output.WriteLine(JsonSerializer.Serialize(ready, BatchJsonContext.Default.BatchOutcome));
					++nextToWrite;
					++written;
				}
			}

			if (written > 0) {
				_ = availableWindowSlots.Release(written);
			}
		}

		void Produce()
		{
			try {
				var index = 0;
				while (true) {
					availableWindowSlots.Wait(cancellation.Token);
					var line = input.ReadLine();
					if (line is null) {
						_ = availableWindowSlots.Release();
						return;
					}

					if (string.IsNullOrWhiteSpace(line)) {
						_ = availableWindowSlots.Release();
						continue;
					}

					try {
						work.Add((index++, line), cancellation.Token);
					}
					catch {
						_ = availableWindowSlots.Release();
						throw;
					}
				}
			}
			catch {
				cancellation.Cancel();
				throw;
			}
			finally {
				work.CompleteAdding();
			}
		}

		// A per-line evaluation failure is isolated to that line's outcome, never aborting the run — the
		// documented "one bad student never aborts the run" contract. A failure in EmitInOrder (a closed
		// stdout) is not caught here: it belongs to the whole run and propagates to cancel it.
		BatchOutcome Evaluate(string line)
		{
			try {
				return evaluateLine(line);
			}
			catch (Exception ex) {
				return new("?", null, $"could not evaluate student: {ex.Message}");
			}
		}

		void Consume()
		{
			try {
				foreach (var item in work.GetConsumingEnumerable(cancellation.Token)) {
					EmitInOrder(item.Index, Evaluate(item.Line));
				}
			}
			catch {
				cancellation.Cancel();
				throw;
			}
		}

		const int producerCount = 1;
		var workers = new Action[maxConcurrency + producerCount];
		workers[0] = Produce;
		Array.Fill(workers, Consume, producerCount, maxConcurrency);
		Parallel.Invoke(new() {
			MaxDegreeOfParallelism = workers.Length,
		}, workers);
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

		// Anything reaching here is per-document (rule-load failures are startup failures, caught earlier), so
		// a throwing evaluation lands on this line's outcome carrying its id, never aborting the run.
		try {
			var outcome = engine.EvaluateValidated(document.Student);
			return outcome.Validation.IsValid
				? new(document.Student.Id, outcome.Value, null)
				: new(document.Student?.Id ?? "?", null, string.Join("; ", outcome.Validation.Errors));
		}
		catch (Exception ex) {
			return new(document.Student?.Id ?? "?", null, $"could not evaluate student: {ex.Message}");
		}
	}

	/// <summary>Build the façade over the selected policy's workflows, reporting a load failure as an input error.</summary>
	private static EnrolmentEngine? BuildEngine(
		TextWriter stderr,
		EnrolmentPolicyId policyId,
		Lazy<IReadOnlyList<EnrolmentPolicyDefinition>> policies)
	{
		try {
			return EnrolmentEngine.Create(ResolveSource(policyId, policies), Today);
		}
		catch (Exception ex) when (ex is WorkflowException or CatalogueException or QualificationScaleException
									   or GcseSubjectsException
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
	private static StudentInput? LoadValidStudent(
		string path,
		TextWriter stderr,
		CatalogueData catalogue,
		QualificationScale scale,
		GcseVocabulary gcses)
	{
		if (Load(path, stderr) is not StudentDocument document) {
			return null;
		}

		var errors = StudentValidator.Validate(document.Student, catalogue, scale, gcses);
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
	private static string WorkflowsDirectory() => ShippedLayout.Locate("workflows", RootMarker);

	/// <summary>
	///     Locate the shipped <c>data/</c> directory (carrying the catalogue and DfE matrix) the same way as
	///     <see cref="WorkflowsDirectory" />: prefer the copy beside the executable, else walk up to the root.
	/// </summary>
	private static string DataDirectory() => ShippedLayout.Locate("data", RootMarker);

	/// <summary>
	///     Locate the shipped <c>policies/</c> directory (carrying every auxiliary policy's own
	///     workflows/catalogue/thresholds) the same way as <see cref="WorkflowsDirectory" /> and
	///     <see cref="DataDirectory" />: prefer the copy beside the executable, else walk up to the root.
	/// </summary>
	private static string PoliciesDirectory() => ShippedLayout.Locate("policies", RootMarker);

	private enum Output
	{
		Table,
		Json,
		Explain,
		ExplainText,
		Advise,
	}
}
