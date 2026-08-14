namespace EnrolmentRules.Web.Api;

using Domain;
using Engine;
using Models;

/// <summary>
///     Builds the client-friendly <see cref="EnrolmentEvaluateResponse" /> from the library's non-destructive <see cref="PolicyComparisonResult" />
///     .
/// </summary>
public static class EnrolmentEvaluateResponseFactory
{
	public static EnrolmentEvaluateResponse Create(ValidatedEvaluation<PolicyComparisonResult> comparison)
	{
		ArgumentNullException.ThrowIfNull(comparison);

		if (!comparison.Validation.IsValid || comparison.Value is not PolicyComparisonResult value) {
			return new([.. comparison.Validation.Errors], null);
		}

		var result = value.Explanation;
		var choiceLimitReason = result.Explanations
									  .SelectMany(static explanation => explanation.Overrides)
									  .FirstOrDefault(static adjustment => adjustment.Kind == AdjustmentKind.ChosenSubjectCap)?.Reason;

		return new(
			[],
			new(
				ToPolicyDescriptor(value.Descriptor),
				result.Eligible,
				[.. result.EligibilityReasons],
				choiceLimitReason,
				[.. result.Explanations.Select(ToExplanationResponse)],
				[.. value.ChoiceStatuses.Select(ToChoiceStatusResponse)],
				value.MinChosenALevels,
				value.MaxChosenALevels));
	}

	private static PolicyDescriptorResponse ToPolicyDescriptor(EnrolmentPolicyDescriptor descriptor) =>
		new(descriptor.Id.Value, descriptor.DisplayName);

	private static ChoiceStatusResponse ToChoiceStatusResponse(ChosenSubjectStatus status) => new(
		new(status.Subject.Value, TextFormatting.Prettify(status.Subject.Value)),
		status.Status.ToString(),
		status.Reason);

	private static ExplanationResponse ToExplanationResponse(Explanation explanation) => new(
		new(explanation.Subject.Value, TextFormatting.Prettify(explanation.Subject.Value)),
		explanation.Rating.ToString(),
		RatingDisplay.CssClass(explanation.Rating),
		explanation.Reason,
		explanation.BaseRating.ToString(),
		explanation.BaseReason,
		explanation.Rule,
		explanation.PredictedPoints,
		explanation.EntryEquivalentReason,
		[.. explanation.Overrides.Select(ToAdjustmentResponse)]);

	private static AdjustmentResponse ToAdjustmentResponse(Adjustment adjustment) =>
		new(adjustment.Subject.Value, adjustment.From.ToString(), adjustment.To.ToString(), adjustment.Reason);
}
