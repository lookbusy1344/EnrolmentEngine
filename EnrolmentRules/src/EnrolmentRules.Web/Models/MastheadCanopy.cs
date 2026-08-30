namespace EnrolmentRules.Web.Models;

/// <summary>
///     Scatters the masthead canopy afresh on every page load: a drift of the brand mark's own leaf
///     blowing across the soil band, in three parallax depths.
/// </summary>
public static class MastheadCanopy
{
	public const int ViewBoxWidth = 400;
	public const int ViewBoxHeight = 120;
	public const double MinDurationSeconds = 17;
	public const double MaxDurationSeconds = 34;

	/// <summary>Leaves bleed past the top and bottom edges; the band's overflow:hidden crops them.</summary>
	private const double VerticalBleed = 18;

	// A ReadOnlySpan-returning property can't back a non-constant struct table; this is built once.
	private static IReadOnlyList<DepthBand> Bands { get; } = [
		new(CanopyDepth.Far, 2.4, 3.2, 10, 18, 10),
		new(CanopyDepth.Mid, 3.6, 4.4, 22, 32, 8),
		new(CanopyDepth.Near, 4.8, 5.6, 38, 52, 6),
	];

	/// <summary>Derived from the bands, so the two can never drift apart.</summary>
	public static int LeafCount { get; } = Bands.Sum(band => band.Share);

	public static IReadOnlyList<CanopyLeaf> Generate(Random random)
	{
		ArgumentNullException.ThrowIfNull(random);

		return [.. Bands.SelectMany(band => Enumerable.Range(0, band.Share).Select(_ => Blow(random, band)))];
	}

	private static CanopyLeaf Blow(Random random, DepthBand band)
	{
		var scale = Between(random, band.MinScale, band.MaxScale);
		var driftX = -Between(random, band.MinDrift, band.MaxDrift);
		var reach = scale * CanopyLeaf.UnitCircumradius;

		// The leaf must clear the canopy edges at rest and at the far end of its drift alike, so the
		// span it is placed in is the free width shrunk by its own reach and by how far it travels.
		var centreX = Between(random, reach - driftX, ViewBoxWidth - reach);
		var duration = Between(random, MinDurationSeconds, MaxDurationSeconds);

		return new(
			Round(centreX),
			Round(Between(random, -VerticalBleed, ViewBoxHeight + VerticalBleed)),
			Round(Between(random, -90, 90)),
			Round(scale),
			band.Depth,
			Round(driftX),
			Round(Between(random, 6, 22)),
			Round(Between(random, -50, 50)),
			Round(duration),
			// A negative delay starts the cycle already under way, so the canopy is mid-blow on the
			// first frame instead of every leaf fading up together.
			Round(-Between(random, 0, duration)));
	}

	private static double Between(Random random, double low, double high) => low + random.NextDouble() * (high - low);

	private static double Round(double value) => Math.Round(value, 2);

	private sealed record DepthBand(
		CanopyDepth Depth,
		double MinScale,
		double MaxScale,
		double MinDrift,
		double MaxDrift,
		int Share);
}
