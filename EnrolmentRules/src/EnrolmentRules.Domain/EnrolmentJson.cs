namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     Source-generated (reflection-free) <see cref="System.Text.Json" /> contract for the document
///     boundary. snake_case property names match the §1.1 input document and the §1.7 output; GCSE
///     dictionary keys pass through verbatim (no key policy). Output is indented for human reading —
///     tests parse it back regardless.
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
	WriteIndented = true)]
[JsonSerializable(typeof(StudentDocument))]
[JsonSerializable(typeof(StudentProfile))]
[JsonSerializable(typeof(EnrolmentResult))]
[JsonSerializable(typeof(ExplainedResult))]
[JsonSerializable(typeof(AdviceResult))]
[JsonSerializable(typeof(PolicyThresholds))]
[JsonSerializable(typeof(Qualification))]
// Element/underlying contracts the EquatableArray/EquatableDictionary converters borrow via
// GetTypeInfo — registered explicitly so the wrappers stay reflection-free under source-gen.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Subject))]
[JsonSerializable(typeof(QualificationType))]
[JsonSerializable(typeof(PredictedGrade))]
[JsonSerializable(typeof(TransitionEvidence))]
[JsonSerializable(typeof(Recommendation))]
[JsonSerializable(typeof(Adjustment))]
[JsonSerializable(typeof(Explanation))]
[JsonSerializable(typeof(GradeChange))]
[JsonSerializable(typeof(SubjectAdvice))]
[JsonSerializable(typeof(GateAdvice))]
[JsonSerializable(typeof(Dictionary<string, int>))]
public sealed partial class EnrolmentJsonContext : JsonSerializerContext;

/// <summary>
///     The compact (single-line, null-omitting) contract for <c>--batch</c> JSONL output: one
///     <see cref="BatchOutcome" /> per line, so every line is a complete, self-contained JSON document.
///     Distinct from <see cref="EnrolmentJsonContext" /> only in that it is <em>not</em> indented (JSONL
///     forbids embedded newlines) and drops the absent member of the result/error pair.
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	WriteIndented = false)]
[JsonSerializable(typeof(BatchOutcome))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Subject))]
[JsonSerializable(typeof(Recommendation))]
[JsonSerializable(typeof(Adjustment))]
public sealed partial class BatchJsonContext : JsonSerializerContext;
