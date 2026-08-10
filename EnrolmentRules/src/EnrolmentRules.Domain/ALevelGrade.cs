namespace EnrolmentRules.Domain;

/// <summary>
///     The A-level points scale that predicted grades are expressed on and compared against.
///     This is the <em>target</em> scale for predictions and is deliberately distinct from the
///     GCSE 1–9 input scale — do not conflate the two. The per-subject tier tests in §1.4
///     ("predicted ≥ A / ≥ B / ≥ C") reference <see cref="A" />, <see cref="B" />, <see cref="C" />;
///     predicted points are continuous and clamped to [<see cref="U" />, <see cref="AStar" />].
/// </summary>
public static class ALevelGrade
{
	public const double AStar = 6.0;
	public const double A = 5.0;
	public const double B = 4.0;
	public const double C = 3.0;
	public const double D = 2.0;
	public const double E = 1.0;
	public const double U = 0.0;

	/// <summary>Lowest point on the scale (a U).</summary>
	public const double Min = U;

	/// <summary>Highest point on the scale (an A*).</summary>
	public const double Max = AStar;
}
