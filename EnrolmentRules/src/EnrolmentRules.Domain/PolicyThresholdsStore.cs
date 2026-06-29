namespace EnrolmentRules.Domain;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;

/// <summary>
///     Loader for the runtime policy knobs used by the workflows and the host pipeline. The shipped file
///     is schema-validated and load-time-validated so any drift in the policy surface fails loud at
///     startup rather than silently changing rules semantics.
/// </summary>
public static class PolicyThresholdsStore
{
	public const string ThresholdsFileName = "thresholds.yaml";
	public const string SchemaFileName = "thresholds.schema.json";

	private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> SchemaCache = new();

	public static PolicyThresholds LoadAndValidate(string directory, string? thresholdsPath = null, string? schemaPath = null)
	{
		thresholdsPath ??= Path.Combine(directory, ThresholdsFileName);
		schemaPath ??= Path.Combine(directory, SchemaFileName);

		using var thresholdsStream = File.OpenRead(thresholdsPath);
		using var schemaStream = File.OpenRead(schemaPath);
		return LoadAndValidate(thresholdsStream, schemaStream, thresholdsPath);
	}

	public static PolicyThresholds LoadAndValidate(Stream thresholdsStream, Stream schemaStream, string? thresholdsPath = null)
	{
		using var thresholdsReader = new StreamReader(thresholdsStream, Encoding.UTF8, true, 1024, true);
		using var schemaReader = new StreamReader(schemaStream, Encoding.UTF8, true, 1024, true);
		return LoadAndValidate(thresholdsReader, schemaReader, thresholdsPath);
	}

	public static PolicyThresholds LoadAndValidate(TextReader thresholdsReader, TextReader schemaReader, string? thresholdsPath = null)
	{
		var node = YamlConverter.ToJsonNode(thresholdsReader.ReadToEnd());
		var schemaText = schemaReader.ReadToEnd();
		var schema = SchemaCache.GetOrAdd(
			SchemaCacheKey(schemaText),
			_ => new(() => JsonSchema.FromText(schemaText))).Value;

		using var doc = JsonDocument.Parse(node.ToJsonString());
		var results = schema.Evaluate(doc.RootElement, new() { OutputFormat = OutputFormat.List });
		if (!results.IsValid) {
			throw new PolicyThresholdsException(
				$"Thresholds file '{thresholdsPath ?? ThresholdsFileName}' failed schema validation: {DescribeErrors(results)}");
		}

		try {
			var thresholds = node.Deserialize(EnrolmentJsonContext.Default.PolicyThresholds)
							 ?? throw new FormatException("Thresholds deserialized to null.");
			Validate(thresholds);
			return thresholds;
		}
		catch (Exception ex) when (ex is InvalidDataException or FormatException) {
			throw new PolicyThresholdsException($"Thresholds file '{thresholdsPath ?? ThresholdsFileName}' is invalid: {ex.Message}", ex);
		}
	}

	private static string SchemaCacheKey(string schemaText) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemaText)));

	private static void Validate(PolicyThresholds thresholds)
	{
		const int min = Thresholds.MinGcseGrade;
		const int max = Thresholds.MaxGcseGrade;

		if (thresholds.PassGrade is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException(
				$"pass_grade {thresholds.PassGrade} is out of range ({min}–{max}).");
		}

		if (thresholds.TopEntry is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException(
				$"top_entry {thresholds.TopEntry} is out of range ({min}–{max}).");
		}

		if (thresholds.StrongEntry is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException(
				$"strong_entry {thresholds.StrongEntry} is out of range ({min}–{max}).");
		}

		if (thresholds.StandardEntry is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException(
				$"standard_entry {thresholds.StandardEntry} is out of range ({min}–{max}).");
		}

		if (thresholds.StandardEntry > thresholds.StrongEntry || thresholds.StrongEntry > thresholds.TopEntry) {
			throw new InvalidDataException(
				"Entry thresholds must satisfy standard_entry <= strong_entry <= top_entry.");
		}

		if (thresholds.FurtherMathsAverageEntry is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade
			|| thresholds.HumanitiesAverageEntry is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException("Average entry thresholds must stay within the GCSE scale.");
		}

		if (thresholds.MinDfeGreenProbabilityAtOrAbove is < 0 or > 1
			|| thresholds.MinDfeAmberProbabilityAtOrAbove is < 0 or > 1
			|| thresholds.AmberTariffFactor is < 0 or > 1) {
			throw new InvalidDataException("Probability and tariff factors must be within 0..1.");
		}

		if (thresholds.MinDfeAmberProbabilityAtOrAbove > thresholds.MinDfeGreenProbabilityAtOrAbove) {
			throw new InvalidDataException("Amber probability must not exceed green probability.");
		}

		if (thresholds.AdultAge <= 0) {
			throw new InvalidDataException("adult_age must be positive.");
		}

		// max_green_choices is optional: absent (null) disables the green cap entirely. When present it
		// must be a real cap of at least one.
		if (thresholds.MaxGreenChoices is < 1) {
			throw new InvalidDataException("max_green_choices, when set, must be at least 1.");
		}

		if (thresholds.AdviceMaxGradeCost < 1) {
			throw new InvalidDataException("advice_max_grade_cost must be at least 1.");
		}

		if (thresholds.AdviceMaxSubjectsChanged < 1) {
			throw new InvalidDataException("advice_max_subjects_changed must be at least 1.");
		}

		if (thresholds.AdviceMaxPipelineEvaluations is < 1) {
			throw new InvalidDataException("advice_max_pipeline_evaluations, when set, must be at least 1.");
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
}

/// <summary>A thresholds file failed schema validation or a load-time invariant at startup.</summary>
public sealed class PolicyThresholdsException : EnrolmentDataException
{
	public PolicyThresholdsException() { }

	public PolicyThresholdsException(string message) : base(message) { }

	public PolicyThresholdsException(string message, Exception innerException) : base(message, innerException) { }
}
