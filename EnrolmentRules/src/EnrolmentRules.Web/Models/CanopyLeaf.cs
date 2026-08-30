namespace EnrolmentRules.Web.Models;

/// <summary>How far back in the masthead canopy a leaf sits. Drives its size, drift and tint.</summary>
public enum CanopyDepth
{
	Far,
	Mid,
	Near,
}

/// <summary>
///     One leaf of the masthead canopy, in the canopy SVG's own user units. The leaf is the brand
///     mark's own blade, centred on the origin, so a placement is a centre, an angle and a scale.
/// </summary>
public sealed record CanopyLeaf(
	double CentreX,
	double CentreY,
	double Rotation,
	double Scale,
	CanopyDepth Depth,
	double DriftX,
	double DriftY,
	double Spin,
	double DurationSeconds,
	double DelaySeconds)
{
	/// <summary>
	///     Distance from the leaf's centre to the furthest point of the unit blade. Multiplied by
	///     <see cref="Scale" /> it bounds the leaf at any angle, so placement need not model rotation.
	/// </summary>
	public const double UnitCircumradius = 7.5;
}
