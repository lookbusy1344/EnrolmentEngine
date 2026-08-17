namespace EnrolmentRules.Domain.Authoring;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;
using Serialization;

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
		try {
			var node = YamlConverter.ToJsonNode(thresholdsReader.ReadToEnd());
			var schemaText = schemaReader.ReadToEnd();
			var schema = SchemaCache.GetOrAdd(
				SchemaCacheKey(schemaText),
				_ => new(() => JsonSchema.FromText(schemaText))).Value;

			using var doc = JsonDocument.Parse(node.ToJsonString());
			var results = schema.Evaluate(doc.RootElement, new() {
				OutputFormat = OutputFormat.List,
			});
			if (!results.IsValid) {
				throw new PolicyThresholdsException(
					$"Thresholds file '{thresholdsPath ?? ThresholdsFileName}' failed schema validation: {DescribeErrors(results)}");
			}

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

		if (thresholds.StandardEntry is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException(
				$"standard_entry {thresholds.StandardEntry} is out of range ({min}–{max}).");
		}

		if (thresholds.ExceptionalEntry is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException(
				$"exceptional_entry {thresholds.ExceptionalEntry} is out of range ({min}–{max}).");
		}

		if (thresholds.StandardEntry > thresholds.TopEntry || thresholds.TopEntry > thresholds.ExceptionalEntry) {
			throw new InvalidDataException(
				"Entry thresholds must satisfy standard_entry <= top_entry <= exceptional_entry.");
		}

		if (thresholds.MinDfeGreenProbabilityAtOrAbove is < 0 or > 1
			|| thresholds.MinDfeAmberProbabilityAtOrAbove is < 0 or > 1
			|| thresholds.AmberScoreFactor is < 0 or > 1) {
			throw new InvalidDataException("Probability and score factors must be within 0..1.");
		}

		if (thresholds.MinDfeAmberProbabilityAtOrAbove > thresholds.MinDfeGreenProbabilityAtOrAbove) {
			throw new InvalidDataException("Amber probability must not exceed green probability.");
		}

		if (thresholds.MaxChosenALevels < 1) {
			throw new InvalidDataException("max_chosen_a_levels must be at least 1.");
		}

		if (thresholds.HighAttainmentMaxChosenALevels < 1) {
			throw new InvalidDataException("high_attainment_max_chosen_a_levels must be at least 1.");
		}

		if (thresholds.HighAttainmentMaxChosenALevels < thresholds.MaxChosenALevels) {
			throw new InvalidDataException(
				"high_attainment_max_chosen_a_levels must be greater than or equal to max_chosen_a_levels.");
		}

		if (thresholds.HighAttainmentAverageGcse is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException("high_attainment_average_gcse must stay within the GCSE scale.");
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

		ValidateTopNGcseKnobs(thresholds);

		if (thresholds.MinChosenALevels is < 0) {
			throw new InvalidDataException("min_chosen_a_levels must not be negative.");
		}

		if (thresholds.MinChosenALevels > thresholds.HighAttainmentMaxChosenALevels) {
			throw new InvalidDataException(
				"min_chosen_a_levels must not exceed high_attainment_max_chosen_a_levels.");
		}
	}

	private static void ValidateTopNGcseKnobs(PolicyThresholds thresholds)
	{
		var set = new[] {
			thresholds.BestGcseCount is not null, thresholds.MinBestGcsePoints is not null, thresholds.TopGcseAverageCount is not null, thresholds.MinTopGcseAverage is not null,
		};
		if (set.Any(static s => s) && !set.All(static s => s)) {
			throw new InvalidDataException(
				"best_gcse_count, min_best_gcse_points, top_gcse_average_count and min_top_gcse_average must be set all four together or not at all.");
		}

		if (thresholds.BestGcseCount is not int bestGcseCount) {
			return;
		}

		var minBestGcsePoints = thresholds.MinBestGcsePoints!.Value;
		var topGcseAverageCount = thresholds.TopGcseAverageCount!.Value;
		var minTopGcseAverage = thresholds.MinTopGcseAverage!.Value;

		if (bestGcseCount < 1) {
			throw new InvalidDataException("best_gcse_count must be at least 1.");
		}

		if (topGcseAverageCount < 1) {
			throw new InvalidDataException("top_gcse_average_count must be at least 1.");
		}

		if (topGcseAverageCount > bestGcseCount) {
			throw new InvalidDataException("top_gcse_average_count must not exceed best_gcse_count.");
		}

		if (minTopGcseAverage is < Thresholds.MinGcseGrade or > Thresholds.MaxGcseGrade) {
			throw new InvalidDataException(
				$"min_top_gcse_average must stay within the GCSE scale ({Thresholds.MinGcseGrade}–{Thresholds.MaxGcseGrade}).");
		}

		var reachableMaximum = bestGcseCount * Thresholds.MaxGcseGrade;
		if (minBestGcsePoints < 1 || minBestGcsePoints > reachableMaximum) {
			throw new InvalidDataException(
				$"min_best_gcse_points {minBestGcsePoints} must be a reachable total for best_gcse_count {bestGcseCount} GCSEs (1–{reachableMaximum}).");
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
