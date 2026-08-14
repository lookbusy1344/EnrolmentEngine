namespace EnrolmentRules.Web.Api;

using Infrastructure;

/// <summary>A single picker option: the value posted back to <c>/api/enrolment/evaluate</c>, and its display label.</summary>
public sealed record OptionItem(string Value, string Label);

/// <summary>One registered policy's wire identifier and display label — the client's own list, never hard-coded.</summary>
public sealed record PolicyDescriptorResponse(string Id, string DisplayName);

/// <summary>The Grade dropdown options for one <see cref="Domain.QualificationType" />, keyed by its wire name (e.g. <c>"BtecDiploma"</c>).</summary>
public sealed record QualificationGradeOptions(string Type, EquatableArray<OptionItem> Grades);

/// <summary>
///     One labelled section of the prior-qualification Subject dropdown, keyed by the exact
///     <see cref="Domain.QualificationType" /> it represents (e.g. <c>"BtecDiploma"</c>, label "BTEC
///     Diploma examples"). The client infers Type from whichever group the chosen subject belongs to,
///     rather than asking for it directly — the two BTEC sub-types share nothing but a label prefix, so
///     Type cannot be collapsed into "which of three buckets did they pick".
/// </summary>
public sealed record QualificationSubjectGroup(string Type, string Label, EquatableArray<OptionItem> Subjects);

/// <summary>Every option and default the Vue app needs to render a facts form without duplicating catalogue knowledge.</summary>
public sealed record EnrolmentOptionsResponse(
	PolicyDescriptorResponse SelectedPolicy,
	EquatableArray<PolicyDescriptorResponse> AvailablePolicies,
	DateOnly DefaultDateOfBirth,
	int DefaultAge,
	EquatableArray<OptionItem> GcseSubjects,
	EquatableArray<OptionItem> ALevelSubjects,
	EquatableArray<QualificationSubjectGroup> PriorQualificationSubjects,
	EquatableArray<QualificationGradeOptions> QualificationGrades,
	EquatableArray<OptionItem> Hobbies,
	int MinChoices,
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

/// <summary>
///     One <c>chosenALevels</c> entry's non-destructive comparison status under the selected policy:
///     <c>"Available"</c> (offered and currently green/amber), <c>"Unavailable"</c> (offered but currently
///     red), or <c>"NotOffered"</c> (absent from the selected policy's catalogue). Never a reason to drop
///     the choice from the client's basket — see <see cref="EnrolmentApiResult.ChoiceStatuses" />.
/// </summary>
public sealed record ChoiceStatusResponse(OptionItem Subject, string Status, string? Reason);

/// <summary>The client-friendly mirror of <c>ExplainedResult</c>, present only when the posted snapshot validated.</summary>
public sealed record EnrolmentApiResult(
	PolicyDescriptorResponse Policy,
	bool Eligible,
	EquatableArray<string> EligibilityReasons,
	string? ChoiceLimitReason,
	EquatableArray<ExplanationResponse> Explanations,
	EquatableArray<ChoiceStatusResponse> ChoiceStatuses,
	int MinChoices,
	int MaxChoices);

/// <summary>
///     The full <c>POST /api/enrolment/evaluate</c> response: either <see cref="Result" />, or
///     <see cref="ValidationErrors" />, never both. No choice is ever ejected server-side: a chosen subject
///     that has gone red under the selected policy, or is absent from its catalogue, stays in
///     <see cref="EnrolmentApiResult.ChoiceStatuses" /> annotated rather than silently dropped — the client
///     owns the basket and decides whether to remove anything.
/// </summary>
public sealed record EnrolmentEvaluateResponse(
	EquatableArray<string> ValidationErrors,
	EnrolmentApiResult? Result);
