namespace EnrolmentRules.Web.Api;

using Infrastructure;

/// <summary>A single picker option: the value posted back to <c>/api/enrolment/evaluate</c>, and its display label.</summary>
public sealed record OptionItem(string Value, string Label);

/// <summary>Every option and default the Vue app needs to render a facts form without duplicating catalogue knowledge.</summary>
public sealed record EnrolmentOptionsResponse(
	DateOnly DefaultDateOfBirth,
	int DefaultAge,
	EquatableArray<OptionItem> GcseSubjects,
	EquatableArray<OptionItem> ALevelSubjects,
	EquatableArray<OptionItem> PriorQualificationSubjects,
	EquatableArray<OptionItem> QualificationTypes,
	EquatableArray<OptionItem> Hobbies,
	int ChoiceLimit);

/// <summary>One posted GCSE row. A row with a blank <see cref="Subject" /> is dropped by the mapper, matching the Razor form's blank-row behaviour.</summary>
public sealed record EvaluateGcseRow(string? Subject, int? Grade);

/// <summary>
///     One posted prior-qualification row. <see cref="Type" /> is the wire name of a <c>QualificationType</c>
///     member (e.g. <c>"BtecDiploma"</c>); an unrecognised non-blank value cannot be mapped into a
///     <c>StudentInput</c> at all, so the endpoint responds 400 rather than attempting engine validation.
/// </summary>
public sealed record EvaluatePriorQualificationRow(string? Subject, string? Type, string? Grade);

/// <summary>The full stateless snapshot <c>POST /api/enrolment/evaluate</c> accepts — every editable fact, every call.</summary>
public sealed record EnrolmentEvaluateRequest(
	DateOnly? DateOfBirth,
	EquatableArray<EvaluateGcseRow> Gcses,
	EquatableArray<EvaluatePriorQualificationRow> PriorQualifications,
	EquatableArray<string> Hobbies,
	EquatableArray<string> ChosenALevels);

/// <summary>One override applied to a subject's base rating (restudy bar, exclusion, own-time veto, choice cap, etc.).</summary>
public sealed record AdjustmentResponse(string Subject, string From, string To, string Reason);

/// <summary>The client-friendly mirror of <c>Explanation</c>: display labels and CSS classes alongside the engine's verdict.</summary>
public sealed record ExplanationResponse(
	OptionItem Subject,
	string Rating,
	string RatingCssClass,
	string Reason,
	string BaseRating,
	string BaseReason,
	string Rule,
	double PredictedPoints,
	string? EntryEquivalentReason,
	EquatableArray<AdjustmentResponse> Overrides);

/// <summary>The client-friendly mirror of <c>ExplainedResult</c>, present only when the posted snapshot validated.</summary>
public sealed record EnrolmentApiResult(
	bool Eligible,
	EquatableArray<string> EligibilityReasons,
	string? ChoiceLimitReason,
	EquatableArray<ExplanationResponse> Explanations);

/// <summary>
///     The full <c>POST /api/enrolment/evaluate</c> response: either <see cref="Result" />, or
///     <see cref="ValidationErrors" />, never both. <see cref="EjectedChoices" /> is non-empty only in the
///     error case, and only when the cause was a posted <c>chosenALevels</c> entry the engine now rates red:
///     it names those subjects so the client can drop them from its basket and re-post, rather than parsing
///     them back out of the error text.
/// </summary>
public sealed record EnrolmentEvaluateResponse(
	EquatableArray<string> ValidationErrors,
	EquatableArray<OptionItem> EjectedChoices,
	EnrolmentApiResult? Result);
