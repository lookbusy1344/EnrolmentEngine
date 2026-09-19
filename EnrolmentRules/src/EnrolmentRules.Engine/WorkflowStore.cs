namespace EnrolmentRules.Engine.Authoring;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Domain;
using Prediction;
using RulesEngine;
using RulesEngine.Interfaces;
using RulesEngine.Models;

/// <summary>
///     Loads, schema-validates and probe-compiles the rules-as-data workflow files, then builds the
///     (reusable, stateless) RulesEngine. All three guards run at startup so a bad workflow — structural
///     or lambda-level — fails loud at boot rather than silently mis-enrolling a student (Reservation 1).
/// </summary>
internal static class WorkflowStore
{
	internal const string SchemaFileName = "workflow.schema.json";

	private static readonly JsonSerializerOptions WorkflowSerializerOptions = new() {
		PropertyNameCaseInsensitive = true,
		Converters = {
			new JsonStringEnumConverter(),
		},
	};

	/// <summary>
	///     Read every workflow file (<c>*.json</c>, <c>*.yaml</c> or <c>*.yml</c>) in
	///     <paramref name="directory" /> (excluding the schema itself), validate each against
	///     <c>workflow.schema.json</c>, and deserialize to RulesEngine workflows.
	///     Throws <see cref="WorkflowSchemaException" /> on the first structural violation.
	/// </summary>
	internal static IReadOnlyList<Workflow> LoadAndValidate(string directory, string? schemaPath = null)
	{
		schemaPath ??= Path.Combine(directory, SchemaFileName);

		var files = Directory.EnumerateFiles(directory)
							 .Where(IsWorkflowFile)
							 .OrderBy(f => f, StringComparer.Ordinal)
							 .ToList();

		using var schemaReader = File.OpenText(schemaPath);
		var readers = new List<(string FileName, TextReader Content)>(files.Count);
		try {
			foreach (var file in files) {
				readers.Add((file, File.OpenText(file)));
			}

			return LoadAndValidate(readers, schemaReader);
		}
		finally {
			foreach (var (_, content) in readers) {
				content.Dispose();
			}
		}
	}

	/// <summary>
	///     Read, schema-validate and deserialize workflow files from arbitrary text readers.
	/// </summary>
	internal static IReadOnlyList<Workflow> LoadAndValidate(
		IReadOnlyList<(string FileName, TextReader Content)> files,
		TextReader schemaReader)
	{
		var schemaText = schemaReader.ReadToEnd();

		var workflows = new List<Workflow>(files.Count);
		foreach (var (file, content) in files) {
			var json = NormalizeWorkflowDocument(file, content.ReadToEnd());
			var document = JsonNode.Parse(json)
						   ?? throw new WorkflowSchemaException(file, "workflow document must be an object, not null");
			SchemaValidator.Validate(
				document, schemaText, errors => new WorkflowSchemaException(file, errors));

			var workflow = JsonSerializer.Deserialize<Workflow>(json, WorkflowSerializerOptions)
						   ?? throw new WorkflowSchemaException(file, "workflow deserialized to null");
			workflows.Add(workflow);
		}

		return workflows;
	}

	/// <summary>
	///     Read, schema-validate and deserialize workflow files from arbitrary streams.
	/// </summary>
	internal static IReadOnlyList<Workflow> LoadAndValidate(
		IReadOnlyList<WorkflowContent> files,
		Stream schemaStream)
	{
		using var schemaReader = new StreamReader(schemaStream, Encoding.UTF8, true, 1024, true);
		var readers = files
					  .Select(file => (file.FileName, (TextReader)new StreamReader(file.Content, Encoding.UTF8, true, 1024, true)))
					  .ToList();
		try {
			return LoadAndValidate(readers, schemaReader);
		}
		finally {
			foreach (var (_, content) in readers) {
				content.Dispose();
			}
		}
	}

	internal static string NormalizeWorkflowDocument(string file, string content) =>
		Path.GetExtension(file) switch {
			".json" => content,
			".yaml" or ".yml" => ConvertYamlToJson(file, content),
			_ => throw new WorkflowSchemaException(file, "unsupported workflow file extension"),
		};

	/// <summary>
	///     Construct the reusable, thread-safe, stateless engine over the validated workflows.
	///     Uses <see cref="RuleSettings.Default" /> so the workflow lambdas may call the registered host
	///     accessor types; callers may override the settings for fixtures that need none.
	/// </summary>
	internal static IRulesEngine BuildEngine(IReadOnlyList<Workflow> workflows, ReSettings? settings = null) =>
		new RulesEngine([.. workflows], settings ?? RuleSettings.Default);

	/// <summary>
	///     Load and schema-validate the workflow files in <paramref name="directory" /> once, returning a
	///     reusable handle that builds the engine on demand. Keeps the untyped RulesEngine workflow type inside
	///     the engine boundary for callers that want to rebuild the engine repeatedly (e.g. a benchmark that
	///     isolates build cost from load).
	/// </summary>
	internal static ReusableWorkflowSet LoadReusable(string directory, string? schemaPath = null) =>
		new(LoadAndValidate(directory, schemaPath));

	/// <summary>
	///     The production startup path when the workflow files are already open as streams: load, schema-validate,
	///     build the reusable engine, then probe-compile every workflow against a canonical fully-populated input
	///     built from the supplied policy and scale.
	/// </summary>
	internal static (IRulesEngine Engine, IReadOnlyList<Workflow> Workflows) LoadValidateBuildAndProbe(
		IReadOnlyList<WorkflowContent> files,
		Stream schemaStream,
		CatalogueData catalogue,
		PolicyThresholds thresholds,
		DfeTransitionMatrix? matrix = null,
		QualificationScale? scale = null,
		GcseVocabulary? gcses = null)
	{
		var vocabulary = gcses ?? GcseVocabulary.Default;
		var workflows = LoadAndValidate(files, schemaStream);
		ThrowOnLintErrors(workflows, catalogue, vocabulary);
		var engine = BuildEngine(workflows);
		ProbeCompile(engine, workflows,
			CanonicalProbe(thresholds, catalogue, matrix ?? DfeTransitionMatrix.LoadDefault(), scale ?? QualificationScale.Default, vocabulary));
		return new(engine, workflows);
	}

	/// <summary>
	///     Force eager lambda compilation by executing every workflow once against a canonical probe input.
	///     A compilation/binding error (typo'd field, malformed expression) surfaces as a non-empty
	///     <see cref="RuleResultTree.ExceptionMessage" />; we turn that into a loud
	///     <see cref="WorkflowProbeException" /> at startup.
	/// </summary>
	internal static void ProbeCompile(
		IRulesEngine engine,
		IEnumerable<Workflow> workflows,
		params RuleParameter[] probeInputs)
	{
		foreach (var workflow in workflows) {
			List<RuleResultTree> results;
			try {
				results = ExecuteAllRules(engine, workflow.WorkflowName, probeInputs);
			}
			catch (Exception ex) {
				throw new WorkflowProbeException(workflow.WorkflowName, ex.Message, ex);
			}

			var failures = results
						   .SelectMany(Flatten)
						   .Where(r => !string.IsNullOrWhiteSpace(r.ExceptionMessage))
						   .Select(r => $"{r.Rule.RuleName}: {r.ExceptionMessage}")
						   .ToList();

			if (failures.Count > 0) {
				throw new WorkflowProbeException(workflow.WorkflowName, string.Join("; ", failures));
			}
		}
	}

	private static List<RuleResultTree> ExecuteAllRules(IRulesEngine engine, string workflow, params RuleParameter[] facts)
	{
		var execution = engine.ExecuteAllRulesAsync(workflow, facts);
		if (execution.IsCompleted) {
#pragma warning disable VSTHRD002 // The completion guard above guarantees this ValueTask has already finished.
			return execution.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
		}

		throw new InvalidOperationException(
			$"RulesEngine workflow '{workflow}' did not complete synchronously during startup probe compilation.");
	}

	private static IEnumerable<RuleResultTree> Flatten(RuleResultTree result)
	{
		yield return result;
		if (result.ChildResults is null) {
			yield break;
		}

		foreach (var child in result.ChildResults.SelectMany(Flatten)) {
			yield return child;
		}
	}

	private static bool IsWorkflowFile(string file) =>
		!string.Equals(Path.GetFileName(file), SchemaFileName, StringComparison.OrdinalIgnoreCase)
		&& Path.GetExtension(file) is ".json" or ".yaml" or ".yml";

	private static void ThrowOnLintErrors(IReadOnlyList<Workflow> workflows, CatalogueData catalogue, GcseVocabulary gcses)
	{
		var findings = WorkflowLinter.Lint(workflows, catalogue, gcses)
									 .Where(static finding => finding.Severity == LintSeverity.Error)
									 .ToArray();
		if (findings.Length > 0) {
			throw new WorkflowLintException(findings);
		}
	}

	// Reuse the shared YAML→JSON normalization (YamlConverter), then layer on the workflow-only default
	// (the implicit LambdaExpression rule type). A generic parse failure is mapped onto the workflow
	// error contract so a bad workflow still fails loud with its file name at startup.
	private static string ConvertYamlToJson(string file, string yaml)
	{
		try {
			var jsonNode = YamlConverter.ToJsonNode(yaml);
			ApplyWorkflowDefaults(file, jsonNode);
			return jsonNode.ToJsonString();
		}
		catch (Exception ex) when (ex is not WorkflowException) {
			throw new WorkflowSchemaException($"Workflow file '{file}' could not parse YAML: {ex.Message}", ex);
		}
	}

	private static void ApplyWorkflowDefaults(string file, JsonNode node)
	{
		switch (node) {
			case JsonObject @object:
				if (@object.ContainsKey("RuleName")
					&& @object.ContainsKey("Expression")
					&& !@object.ContainsKey("RuleExpressionType")) {
					@object["RuleExpressionType"] = "LambdaExpression";
				}

				foreach (var property in @object) {
					if (property.Value is not null) {
						ApplyWorkflowDefaults(file, property.Value);
					}
				}

				break;
			case JsonArray array:
				foreach (var item in array) {
					if (item is not null) {
						ApplyWorkflowDefaults(file, item);
					}
				}

				break;
			case JsonValue:
				break;
			default:
				throw new WorkflowSchemaException(file, $"unsupported JSON node type '{node.GetType().Name}'");
		}
	}

	private static RuleParameter[] CanonicalProbe(
		PolicyThresholds thresholds,
		CatalogueData catalogue,
		DfeTransitionMatrix matrix,
		QualificationScale scale,
		GcseVocabulary vocabulary)
	{
		var student = CanonicalProbeStudent(thresholds, vocabulary);
		var gcses = student.ToGcseResults();
		var lookup = new GcseFacts(gcses);
		var profile = GradePredictor.Predict(student, gcses, default, catalogue, matrix, scale);

		return [
			.. RatingEvaluator.EligibilityParameters(gcses, lookup, new(thresholds)),
			new("facts", new RatingFacts(profile, lookup, new(thresholds), catalogue, scale)),
		];
	}

	// The canonical probe student: every recognised GCSE subject populated at the top entry grade, derived
	// from the loaded GCSE vocabulary so it stays fully populated as the vocabulary changes rather than
	// tracking a hand-maintained literal list. The grades only need to be present (the probe forces lambda
	// compilation, not a particular verdict), so a uniform passing grade suffices.
	internal static StudentInput CanonicalProbeStudent(PolicyThresholds thresholds, GcseVocabulary? gcses = null) =>
		new(
			"probe",
			EquatableDictionaryFactory.CopyOf((gcses ?? GcseVocabulary.Default).Known.ToDictionary(static subject => subject, _ => thresholds.TopEntry,
				StringComparer.Ordinal)),
			[]);
}
