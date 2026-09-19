namespace EnrolmentRules.Domain;

using System.Collections.Immutable;

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

	/// <summary>The display label for the top grade — the one token that is not its own upper-cased self.</summary>
	public const string AStarLabel = "A*";

	/// <summary>The grade names against their points, highest first. The single source for narration and rendering.</summary>
	private static ImmutableArray<(double Points, string Name)> Bands { get; } = [
		(AStar, AStarLabel), (A, "A"), (B, "B"), (C, "C"), (D, "D"), (E, "E"), (U, "U"),
	];

	/// <summary>The grade name for an exact point on the scale.</summary>
	public static bool TryName(double points, out string name)
	{
		foreach (var band in Bands) {
			if (band.Points == points) {
				name = band.Name;
				return true;
			}
		}

		name = string.Empty;
		return false;
	}

	/// <summary>The grade name for an exact point on the scale; throws when <paramref name="points" /> is off-grid.</summary>
	public static string Name(double points) =>
		TryName(points, out var name)
			? name
			: throw new ArgumentOutOfRangeException(nameof(points), points, "Not a point on the A-level grade scale.");

	/// <summary>The band nearest <paramref name="points" />, ties broken towards the higher grade — the grade a continuous prediction rounds to for display.</summary>
	public static (double Points, string Name) NearestBand(double points)
	{
		var best = Bands[0];
		var bestDistance = Math.Abs(best.Points - points);
		foreach (var band in Bands) {
			var distance = Math.Abs(band.Points - points);
			if (distance < bestDistance) {
				(best, bestDistance) = (band, distance);
			}
		}

		return best;
	}
}
