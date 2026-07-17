namespace EnrolmentRules.Web.Api;

using Domain;
using Models;

/// <summary>Builds the client-friendly <see cref="EnrolmentEvaluateResponse" /> from the engine's <see cref="ValidatedEvaluation{T}" />.</summary>
public static class EnrolmentEvaluateResponseFactory
{
	public static EnrolmentEvaluateResponse Create(
		ValidatedEvaluation<ExplainedResult> evaluation,
		IReadOnlyList<Subject> ejectedChoices)
	{
		ArgumentNullException.ThrowIfNull(evaluation);
		ArgumentNullException.ThrowIfNull(ejectedChoices);

		if (!evaluation.Validation.IsValid || evaluation.Value is not { } result) {
			return new(
				[.. evaluation.Validation.Errors],
				[.. ejectedChoices.Select(static subject => new OptionItem(subject.Value, TextFormatting.Prettify(subject.Value)))],
				null);
		}

		var choiceLimitReason = result.Explanations
			.SelectMany(static explanation => explanation.Overrides)
			.FirstOrDefault(static adjustment => adjustment.Kind == AdjustmentKind.ChosenSubjectCap)?.Reason;

		return new(
			[],
			[],
			new(
				result.Eligible,
				[.. result.EligibilityReasons],
				choiceLimitReason,
				[.. result.Explanations.Select(ToExplanationResponse)]));
	}

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
