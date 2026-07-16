namespace EnrolmentRules.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Domain;

/// <summary>
///     The build stamp surfaced by the CLI and the web front-end. The commit comes from
///     <c>SourceRevisionId</c>, which the SDK folds into <see cref="AssemblyInformationalVersionAttribute" />
///     as the <c>+metadata</c> suffix; a build from a source drop with no git checkout degrades to
///     <see cref="BuildInfo.UnknownCommit" /> rather than failing.
/// </summary>
public sealed partial class BuildInfoTests
{
	[GeneratedRegex("^[0-9a-f]{7,40}$")]
	private static partial Regex CommitPattern();

	[Fact]
	public void the_commit_is_a_git_hash_or_the_unknown_marker()
	{
		if (BuildInfo.Commit != BuildInfo.UnknownCommit) {
			BuildInfo.Commit.Should().MatchRegex(CommitPattern().ToString());
		}
	}

	[Fact]
	public void the_version_is_the_assembly_version_without_build_metadata()
	{
		BuildInfo.Version.Should().NotBeNullOrWhiteSpace();
		BuildInfo.Version.Should().NotContain("+", "build metadata belongs in Commit, not Version");
	}

	[Fact]
	public void the_commit_stays_bare_so_it_can_address_a_commit_url() => BuildInfo.Commit.Should().NotEndWith("-dirty");

	[Fact]
	public void the_full_stamp_joins_the_version_and_the_commit_and_marks_an_uncommitted_build()
	{
		var expected = BuildInfo.IsDirty
			? $"{BuildInfo.Version}+{BuildInfo.Commit}-dirty"
			: $"{BuildInfo.Version}+{BuildInfo.Commit}";

		BuildInfo.VersionWithCommit.Should().Be(expected);
	}

	[Fact]
	public void a_local_build_stamps_the_real_commit()
	{
		// The test suite is built from this git checkout, so the MSBuild stamp must have fired.
		BuildInfo.Commit.Should().NotBe(BuildInfo.UnknownCommit);
	}
}
