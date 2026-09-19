namespace EnrolmentRules.Tests;

using AwesomeAssertions;

/// <summary>
///     F3 — policy discovery from the shipped directory layout. The base Standard definition first, then one
///     overlay per <c>policies/*/</c> subdirectory ordered by id, each named from its <c>policy.yaml</c>
///     manifest. Dropping a folder in makes a policy selectable with no code change.
/// </summary>
public sealed class PolicyDirectoryLayoutTests
{
	private static string NewTree(params (string Id, string? DisplayName)[] policies)
	{
		var root = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(root, "workflows"));
		var dataDir = Path.Combine(root, "data");
		Directory.CreateDirectory(dataDir);
		File.Copy(
			Path.Combine(Harness.DataDir, PolicyDirectoryLayout.ManifestSchemaFileName),
			Path.Combine(dataDir, PolicyDirectoryLayout.ManifestSchemaFileName));

		foreach (var (id, displayName) in policies) {
			var policyDir = Path.Combine(root, "policies", id);
			Directory.CreateDirectory(policyDir);
			if (displayName is not null) {
				File.WriteAllText(Path.Combine(policyDir, PolicyDirectoryLayout.ManifestFileName), $"display_name: {displayName}\n");
			}
		}

		return root;
	}

	private static IReadOnlyList<EnrolmentPolicyDefinition> Discover(string root) =>
		PolicyDirectoryLayout.Discover(
			Path.Combine(root, "workflows"), Path.Combine(root, "data"), Path.Combine(root, "policies"));

	[Fact]
	public void discovery_returns_standard_first_then_one_overlay_per_folder_in_id_order()
	{
		var root = NewTree(("zebra", "Zebra"), ("aardvark", "Aardvark"));
		try {
			var definitions = Discover(root);

			definitions.Select(d => d.Id.Value).Should().Equal("standard", "aardvark", "zebra");
			definitions.Select(d => d.DisplayName).Should().Equal("Standard", "Aardvark", "Zebra");
		}
		finally {
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void a_policy_folder_without_a_manifest_is_a_configuration_error_naming_the_folder()
	{
		var root = NewTree(("broken", null));
		try {
			var act = () => Discover(root);

			act.Should().Throw<EnrolmentPolicyConfigurationException>().WithMessage("*broken*");
		}
		finally {
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void malformed_manifest_is_a_configuration_error_naming_the_file()
	{
		var root = NewTree(("broken", "Broken"));
		try {
			var manifest = Path.Combine(root, "policies", "broken", PolicyDirectoryLayout.ManifestFileName);
			File.WriteAllText(manifest, "display_name: [");
			var act = () => Discover(root);

			act.Should().Throw<EnrolmentPolicyConfigurationException>()
			   .WithMessage("*broken*policy.yaml*");
		}
		finally {
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void invalid_policy_directory_id_is_a_configuration_error_naming_the_folder()
	{
		var root = NewTree(("Invalid_Id", "Invalid"));
		try {
			var act = () => Discover(root);

			act.Should().Throw<EnrolmentPolicyConfigurationException>()
			   .WithMessage("*Invalid_Id*");
		}
		finally {
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void discovery_returns_only_standard_when_no_policies_directory_exists()
	{
		var root = NewTree();
		try {
			Discover(root).Select(d => d.Id.Value).Should().Equal("standard");
		}
		finally {
			Directory.Delete(root, true);
		}
	}
}
