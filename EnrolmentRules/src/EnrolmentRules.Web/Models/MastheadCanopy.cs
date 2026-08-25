namespace EnrolmentRules.Web.Models;

/// <summary>
/// Scatters the masthead canopy afresh on every page load: a drift of the brand mark's own leaf
/// blowing across the soil band, in three parallax depths.
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
	private static IReadOnlyList<DepthBand> Bands { get; } =
	[
		new(CanopyDepth.Far, MinScale: 2.4, MaxScale: 3.2, MinDrift: 10, MaxDrift: 18, Share: 10),
		new(CanopyDepth.Mid, MinScale: 3.6, MaxScale: 4.4, MinDrift: 22, MaxDrift: 32, Share: 8),
		new(CanopyDepth.Near, MinScale: 4.8, MaxScale: 5.6, MinDrift: 38, MaxDrift: 52, Share: 6),
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

		return new CanopyLeaf(
			CentreX: Round(centreX),
			CentreY: Round(Between(random, -VerticalBleed, ViewBoxHeight + VerticalBleed)),
			Rotation: Round(Between(random, -90, 90)),
			Scale: Round(scale),
			Depth: band.Depth,
			DriftX: Round(driftX),
			DriftY: Round(Between(random, 6, 22)),
			Spin: Round(Between(random, -50, 50)),
			DurationSeconds: Round(duration),
			// A negative delay starts the cycle already under way, so the canopy is mid-blow on the
			// first frame instead of every leaf fading up together.
			DelaySeconds: Round(-Between(random, 0, duration)));
	}

	private static double Between(Random random, double low, double high) => low + (random.NextDouble() * (high - low));

	private static double Round(double value) => Math.Round(value, 2);

	private sealed record DepthBand(
		CanopyDepth Depth,
		double MinScale,
		double MaxScale,
		double MinDrift,
		double MaxDrift,
		int Share);
}
