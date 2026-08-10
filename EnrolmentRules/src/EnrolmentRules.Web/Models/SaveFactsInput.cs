namespace EnrolmentRules.Web.Models;

using Infrastructure;

/// <summary>
///     The bound shape of the "save facts" form post: everything <c>OnPostSaveFacts</c> can edit in one
///     submit. Deliberately not the ASP.NET model-binding target itself — the page handler binds raw
///     <c>List&lt;T&gt;</c> properties (which the default model binder understands) and constructs this
///     record explicitly, so <see cref="EnrolmentFormMapper" /> can stay a pure function over a
///     value-equatable input.
/// </summary>
public sealed record SaveFactsInput(
	DateOnly? DateOfBirth,
	EquatableArray<GcseRow> Gcses,
	EquatableArray<PriorQualificationRow> PriorQualifications,
	EquatableArray<string> Hobbies);
