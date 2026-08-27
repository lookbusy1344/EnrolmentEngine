namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using EnrolmentRules.Web.Models;

public sealed class MastheadCanopyTests
{
	private const int SeedSweep = 200;

	private static IEnumerable<CanopyLeaf> Sweep() =>
		Enumerable.Range(0, SeedSweep).SelectMany(seed => MastheadCanopy.Generate(new Random(seed)));

	[Fact]
	public void Generate_lays_out_the_declared_number_of_leaves()
	{
		var leaves = MastheadCanopy.Generate(new Random(1));

		leaves.Should().HaveCount(MastheadCanopy.LeafCount);
	}

	// getBoundingClientRect on an SVG path ignores the parent's overflow:hidden, so a leaf that
	// bleeds past the canopy at any point of its drift reads to the browser as page overflow and
	// fails the responsive e2e check at every breakpoint.
	// The translate-only model is sound because site.css pivots each leaf on its own box
	// (transform-box: fill-box): rotation about the leaf's own centre stays within the
	// UnitCircumradius × Scale reach this placement already budgets. A shared pivot
	// (transform-box: view-box) would let rotation carry a leaf outside these bounds.
	[Fact]
	public void Every_leaf_stays_inside_the_canopy_across_its_whole_drift()
	{
		foreach (var leaf in Sweep()) {
			var reach = leaf.Scale * CanopyLeaf.UnitCircumradius;
			var restX = leaf.CentreX;
			var driftedX = leaf.CentreX + leaf.DriftX;

			Math.Min(restX, driftedX).Should().BeGreaterThanOrEqualTo(reach);
			Math.Max(restX, driftedX).Should().BeLessThanOrEqualTo(MastheadCanopy.ViewBoxWidth - reach);
		}
	}

	[Fact]
	public void Leaves_drift_leftwards_into_the_mask_rather_than_out_of_the_open_edge()
	{
		Sweep().Should().OnlyContain(leaf => leaf.DriftX <= 0);
	}

	// Depth is the parallax contract the stylesheet leans on: nearer leaves are drawn larger,
	// brighter, and are blown further than the ones behind them.
	[Fact]
	public void Nearer_leaves_are_larger_and_drift_further_than_the_ones_behind_them()
	{
		var byDepth = Sweep()
			.GroupBy(leaf => leaf.Depth)
			.ToDictionary(group => group.Key, group => group.ToList());

		byDepth.Should().ContainKeys(CanopyDepth.Far, CanopyDepth.Mid, CanopyDepth.Near);

		AssertNearer(byDepth[CanopyDepth.Near], byDepth[CanopyDepth.Mid]);
		AssertNearer(byDepth[CanopyDepth.Mid], byDepth[CanopyDepth.Far]);
	}

	private static void AssertNearer(List<CanopyLeaf> nearer, List<CanopyLeaf> further)
	{
		nearer.Min(leaf => leaf.Scale).Should().BeGreaterThan(further.Max(leaf => leaf.Scale));
		// Drift is leftwards, so blown further means a more negative DriftX.
		nearer.Max(leaf => leaf.DriftX).Should().BeLessThan(further.Min(leaf => leaf.DriftX));
	}

	[Fact]
	public void Every_leaf_animates_on_its_own_slow_offset_cycle()
	{
		foreach (var leaf in Sweep()) {
			leaf.DurationSeconds.Should().BeInRange(MastheadCanopy.MinDurationSeconds, MastheadCanopy.MaxDurationSeconds);
			leaf.DelaySeconds.Should().BeInRange(-leaf.DurationSeconds, 0);
		}
	}

	[Fact]
	public void Successive_page_loads_get_a_different_arrangement()
	{
		var first = MastheadCanopy.Generate(new Random(1));
		var second = MastheadCanopy.Generate(new Random(2));

		second.Should().NotBeEquivalentTo(first);
	}

	[Fact]
	public void The_same_seed_reproduces_the_same_arrangement()
	{
		MastheadCanopy.Generate(new Random(7)).Should().BeEquivalentTo(MastheadCanopy.Generate(new Random(7)));
	}
}
