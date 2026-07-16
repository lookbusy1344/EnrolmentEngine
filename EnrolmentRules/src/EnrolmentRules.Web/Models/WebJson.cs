namespace EnrolmentRules.Web.Models;

using System.Text.Json.Serialization;
using Domain;

/// <summary>
///     Source-generated (reflection-free) <see cref="System.Text.Json" /> contract for the session
///     snapshot serialised by <see cref="Services.EnrolmentSessionStore" />. The web-local
///     <c>EquatableArray{T}</c> converter lives in this assembly (unlike <c>EnrolmentRules.Domain</c>'s
///     internal one), so this context can see it and stay source-generated.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(EnrolmentSession))]
[JsonSerializable(typeof(GcseRow))]
[JsonSerializable(typeof(PriorQualificationRow))]
// Element/underlying contracts the EquatableArray converter borrows via GetTypeInfo — registered
// explicitly so the wrapper stays reflection-free under source-gen.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Subject))]
[JsonSerializable(typeof(QualificationType))]
internal sealed partial class WebJsonContext : JsonSerializerContext;
