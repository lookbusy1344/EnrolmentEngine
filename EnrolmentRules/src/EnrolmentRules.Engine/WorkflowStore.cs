namespace EnrolmentRules.Engine;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Domain;
using Json.Schema;
using Prediction;
using RulesEngine;
using RulesEngine.Interfaces;
using RulesEngine.Models;

/// <summary>
///     Loads, schema-validates and probe-compiles the rules-as-data workflow files, then builds the
///     (reusable, stateless) RulesEngine. All three guards run at startup so a bad workflow — structural
///     or lambda-level — fails loud at boot rather than silently mis-enrolling a student (Reservation 1).
/// </summary>
[CLSCompliant(false)]
public static class WorkflowStore
{
	public const string SchemaFileName = "workflow.schema.json";

	private static readonly JsonSerializerOptions WorkflowSerializerOptions = new() {
		PropertyNameCaseInsensitive = true,
		Converters = { new JsonStringEnumConverter() },
	};

	// JsonSchema.FromFile registers the schema's $id in a process-global registry; loading the same
	// schema content twice throws. Cache the compiled schema per schema text so repeated startups/tests
	// reuse one instance even when the workflow files are loaded from both the source tree and a
	// published output tree.
	// The value is Lazy so the factory runs exactly once per path even under concurrent (parallel-test)
	// access — ConcurrentDictionary.GetOrAdd alone does not guarantee a single factory invocation.
	private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> SchemaCache = new();

	/// <summary>
	///     Read every workflow file (<c>*.json</c>, <c>*.yaml</c> or <c>*.yml</c>) in
	///     <paramref name="directory" /> (excluding the schema itself), validate each against
	///     <c>workflow.schema.json</c>, and deserialize to RulesEngine workflows.
	///     Throws <see cref="WorkflowSchemaException" /> on the first structural violation.
	/// </summary>
	public static IReadOnlyList<Workflow> LoadAndValidate(string directory, string? schemaPath = null)
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
	public static IReadOnlyList<Workflow> LoadAndValidate(
		IReadOnlyList<(string FileName, TextReader Content)> files,
		TextReader schemaReader)
	{
		var schemaText = schemaReader.ReadToEnd();
		var schema = SchemaCache.GetOrAdd(
			SchemaCacheKey(schemaText),
			_ => new(() => JsonSchema.FromText(schemaText))).Value;

		var workflows = new List<Workflow>(files.Count);
		foreach (var (file, content) in files) {
			var json = NormalizeWorkflowDocument(file, content.ReadToEnd());
			using var doc = JsonDocument.Parse(json);

			var results = schema.Evaluate(doc.RootElement, new() { OutputFormat = OutputFormat.List });
			if (!results.IsValid) {
				throw new WorkflowSchemaException(file, DescribeErrors(results));
			}

			var workflow = JsonSerializer.Deserialize<Workflow>(json, WorkflowSerializerOptions)
						   ?? throw new WorkflowSchemaException(file, "workflow deserialized to null");
			workflows.Add(workflow);
		}

		return workflows;
	}

	/// <summary>
	///     Read, schema-validate and deserialize workflow files from arbitrary streams.
	/// </summary>
	public static IReadOnlyList<Workflow> LoadAndValidate(
		IReadOnlyList<(string FileName, Stream Content)> files,
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

	private static string SchemaCacheKey(string schemaText) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemaText)));

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
	public static IRulesEngine BuildEngine(IReadOnlyList<Workflow> workflows, ReSettings? settings = null) =>
		new RulesEngine([.. workflows], settings ?? RuleSettings.Default);

	/// <summary>
	///     The production startup path with an explicit catalogue: load, schema-validate, build the
	///     reusable engine, then probe-compile every workflow against a canonical fully-populated input.
	///     The probe thresholds are inferred from the workflows' sibling <c>data/</c> directory; callers that
	///     already hold the thresholds (and may keep data in a non-sibling location) should pass them via the
	///     <see cref="LoadValidateBuildAndProbeAsync(string, CatalogueData, PolicyThresholds, DfeTransitionMatrix?, QualificationScale?, string?)" />
	///     overload so the probe and the engine agree on one source.
	/// </summary>
	public static Task<IRulesEngine> LoadValidateBuildAndProbeAsync(
		string directory,
		CatalogueData catalogue,
		string? schemaPath = null)
		=> LoadValidateBuildAndProbeAsync(directory, catalogue, LoadDefaultThresholds(directory), null, null, schemaPath);

	/// <summary>
	///     The production startup path with explicit catalogue, thresholds and (optionally) transition matrix:
	///     load, schema-validate, build the reusable engine, then probe-compile every workflow against a
	///     canonical fully-populated input built from the supplied policy. Threading the caller's data keeps the
	///     probe and the engine bound to one source even when the workflows and data directories are not
	///     siblings. The matrix only feeds the probe's transition evidence (irrelevant to lambda compilation),
	///     so it defaults to the shipped extract.
	/// </summary>
	public static async Task<IRulesEngine> LoadValidateBuildAndProbeAsync(
		string directory,
		CatalogueData catalogue,
		PolicyThresholds thresholds,
		DfeTransitionMatrix? matrix = null,
		QualificationScale? scale = null,
		string? schemaPath = null)
	{
		var workflows = LoadAndValidate(directory, schemaPath);
		var engine = BuildEngine(workflows);
		await ProbeCompileAsync(engine, workflows,
				CanonicalProbe(thresholds, catalogue, matrix ?? DfeTransitionMatrix.LoadDefault(), scale ?? QualificationScale.Default))
			.ConfigureAwait(false);
		return engine;
	}

	/// <summary>
	///     The production startup path when the workflow files are already open as streams: load, schema-validate,
	///     build the reusable engine, then probe-compile every workflow against a canonical fully-populated input
	///     built from the supplied policy and scale.
	/// </summary>
	public static async Task<IRulesEngine> LoadValidateBuildAndProbeAsync(
		IReadOnlyList<(string FileName, Stream Content)> files,
		Stream schemaStream,
		CatalogueData catalogue,
		PolicyThresholds thresholds,
		DfeTransitionMatrix? matrix = null,
		QualificationScale? scale = null)
	{
		var workflows = LoadAndValidate(files, schemaStream);
		var engine = BuildEngine(workflows);
		await ProbeCompileAsync(engine, workflows,
				CanonicalProbe(thresholds, catalogue, matrix ?? DfeTransitionMatrix.LoadDefault(), scale ?? QualificationScale.Default))
			.ConfigureAwait(false);
		return engine;
	}

	/// <summary>
	///     Force eager lambda compilation by executing every workflow once against a canonical probe input.
	///     A compilation/binding error (typo'd field, malformed expression) surfaces as a non-empty
	///     <see cref="RuleResultTree.ExceptionMessage" />; we turn that into a loud
	///     <see cref="WorkflowProbeException" /> at startup.
	/// </summary>
	public static async Task ProbeCompileAsync(
		IRulesEngine engine,
		IEnumerable<Workflow> workflows,
		params RuleParameter[] probeInputs)
	{
		foreach (var workflow in workflows) {
			List<RuleResultTree> results;
			try {
				results = await engine.ExecuteAllRulesAsync(workflow.WorkflowName, probeInputs).ConfigureAwait(false);
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

	private static string DescribeErrors(EvaluationResults results)
	{
		var messages = (results.Details ?? [])
			.Where(d => d.Errors is { Count: > 0 })
			.SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"));
		var joined = string.Join("; ", messages);
		return joined.Length > 0 ? joined : "schema validation failed (no detailed errors reported)";
	}

	private static RuleParameter[] CanonicalProbe(
		PolicyThresholds thresholds,
		CatalogueData catalogue,
		DfeTransitionMatrix matrix,
		QualificationScale scale)
	{
		var student = CanonicalProbeStudent(thresholds);
		var gcses = student.ToGcseResults();
		var profile = GradePredictor.Predict(student, gcses, default, catalogue, matrix, scale);

		return [
			.. RatingEvaluator.EligibilityParameters(gcses, thresholds),
			new("facts", new RatingFacts(profile, gcses, new(thresholds), catalogue, scale)),
		];
	}

	// The canonical probe student: every recognised GCSE subject populated at the top entry grade, derived
	// from GcseSubjects.Known so it stays fully populated as the GCSE vocabulary changes rather than tracking
	// a hand-maintained literal list. The grades only need to be present (the probe forces lambda compilation,
	// not a particular verdict), so a uniform passing grade suffices.
	internal static StudentInput CanonicalProbeStudent(PolicyThresholds thresholds) =>
		new(
			"probe",
			GcseSubjects.Known.ToDictionary(static subject => subject, _ => thresholds.TopEntry, StringComparer.Ordinal),
			[]);

	private static PolicyThresholds LoadDefaultThresholds(string workflowsDirectory)
	{
		var root = Directory.GetParent(Path.GetFullPath(workflowsDirectory))?.FullName
				   ?? throw new DirectoryNotFoundException($"Could not resolve repository root from '{workflowsDirectory}'.");

		return PolicyThresholdsStore.LoadAndValidate(Path.Combine(root, "data"));
	}
}
