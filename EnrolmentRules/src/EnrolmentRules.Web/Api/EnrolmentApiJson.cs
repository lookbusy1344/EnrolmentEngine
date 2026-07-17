namespace EnrolmentRules.Web.Api;

using System.Text.Json.Serialization;

/// <summary>
///     Source-generated (reflection-free) <see cref="System.Text.Json" /> contract for
///     <c>/api/enrolment/*</c> request/response bodies. Deliberately separate from
///     <see cref="Models.WebJsonContext" />: that context serialises the snake_case session snapshot, while
///     this one is the camelCase wire shape the Vue client consumes.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OptionItem))]
[JsonSerializable(typeof(EnrolmentOptionsResponse))]
[JsonSerializable(typeof(EnrolmentEvaluateRequest))]
[JsonSerializable(typeof(EnrolmentEvaluateResponse))]
// Element/underlying contracts EquatableArray<T>'s converter borrows via GetTypeInfo — registered
// explicitly so the wrapper stays reflection-free under source-gen.
[JsonSerializable(typeof(EvaluateGcseRow))]
[JsonSerializable(typeof(EvaluatePriorQualificationRow))]
[JsonSerializable(typeof(ExplanationResponse))]
[JsonSerializable(typeof(AdjustmentResponse))]
[JsonSerializable(typeof(string))]
public sealed partial class EnrolmentApiJsonContext : JsonSerializerContext;
