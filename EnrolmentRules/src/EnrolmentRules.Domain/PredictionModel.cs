namespace EnrolmentRules.Domain;

/// <summary>
///     The fixed (not runtime-trained) linear-regression model that maps a student's
///     <c>averageGcseScore</c> (the single feature, on the GCSE 1–9 scale) to a predicted A-level
///     result per subject on the <see cref="ALevelGrade" /> points scale (§1.2). The coefficients
///     themselves live in the loaded catalogue data; this type keeps only the prediction math and its
///     typed value object.
/// </summary>
/// <remarks>
///     A second feature (the subject-relevant GCSE grade) is deliberately parked (plan §5 open
///     questions); average-only keeps the stage a pure, trivially-testable one-feature regression.
///     <para>
///         Maths and Further Maths carry deliberately divergent coefficients: Further Maths is the steeper
///         line, so for the strongest students it can out-predict Maths. That crossover is what makes the
///         Phase-5 prerequisite scenario reachable (Further Maths green/amber while Maths is red), rather
///         than impossible to construct.
///     </para>
/// </remarks>
public static class PredictionModel
{
	/// <summary>
	///     Coefficients of a one-feature line <c>points = (Slope · feature) + Intercept</c>,
	///     clamped to the valid A-level points range.
	/// </summary>
	public readonly record struct Coefficients(double Slope, double Intercept)
	{
		/// <summary>Predict clamped A-level points from an average GCSE score.</summary>
		public double Predict(double averageGcseScore) =>
			Math.Clamp((Slope * averageGcseScore) + Intercept, ALevelGrade.Min, ALevelGrade.Max);
	}
}
