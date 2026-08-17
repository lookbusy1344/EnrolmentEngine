namespace EnrolmentRules.Tests;

using System.Text.RegularExpressions;
using AwesomeAssertions;

/// <summary>
///     F5 — the shipped policy-asset item set (base workflows/schema, catalogue/qualification/threshold
///     data + schemas, the DfE transition matrix, every <c>policies/**</c> auxiliary policy) has exactly
///     one owner for the two executable hosts: <c>build/EnrolmentRules.PolicyAssets.props</c>. A host that re-declares its own glob
///     against <c>workflows/</c>, <c>data/</c>, or <c>policies/</c> defeats that — a new shipped file would
///     then reach some consumers and silently miss others.
/// </summary>
public sealed partial class PolicyAssetOwnershipTests
{
	[GeneratedRegex(
		"""Include\s*=\s*"[^"]*\.\.[\\/](workflows|data|policies)[\\/]""",
		RegexOptions.IgnoreCase)]
	private static partial Regex DirectPolicyAssetGlob();

	[Theory]
	[InlineData("src/EnrolmentRules.Cli/EnrolmentRules.Cli.csproj")]
	[InlineData("src/EnrolmentRules.Web/EnrolmentRules.Web.csproj")]
	[InlineData("src/EnrolmentRules.Engine/EnrolmentRules.Engine.csproj")]
	public void project_files_declare_no_direct_policy_asset_glob(string relativeProjectPath)
	{
		var path = Path.Combine(Harness.RepoRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
		var content = File.ReadAllText(path);

		DirectPolicyAssetGlob().IsMatch(content).Should().BeFalse(
			$"{relativeProjectPath} must import build/EnrolmentRules.PolicyAssets.props for the shipped " +
			"asset set instead of globbing workflows/, data/, or policies/ directly");
	}

	[Theory]
	[InlineData("src/EnrolmentRules.Cli/EnrolmentRules.Cli.csproj")]
	[InlineData("src/EnrolmentRules.Web/EnrolmentRules.Web.csproj")]
	public void project_files_import_the_shared_policy_asset_props(string relativeProjectPath)
	{
		var path = Path.Combine(Harness.RepoRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
		var content = File.ReadAllText(path);

		content.Should().Contain("EnrolmentRules.PolicyAssets.props");
	}

	[Fact]
	public void engine_package_does_not_embed_runtime_policy_assets()
	{
		var path = Path.Combine(Harness.RepoRoot, "src", "EnrolmentRules.Engine", "EnrolmentRules.Engine.csproj");
		var content = File.ReadAllText(path);

		content.Should().NotContain("EnrolmentRules.PolicyAssets.props");
		content.Should().NotContain("EnrolmentPolicyAsset");
		content.Should().NotContain("contentFiles");
	}
}
