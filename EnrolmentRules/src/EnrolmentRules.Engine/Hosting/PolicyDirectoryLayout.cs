namespace EnrolmentRules.Engine.Hosting;

using System.Text.Json.Nodes;

/// <summary>
///     Discovers the registered policy set from the shipped directory layout: the base Standard policy
///     followed by one overlay policy per <c>policies/&lt;id&gt;/</c> subdirectory, each carrying a
///     <c>policy.yaml</c> manifest. Both hosts (CLI and Web) build their policy registry from this one
///     discovery so a new policy is a directory to drop in, not code to edit in two places.
/// </summary>
public static class PolicyDirectoryLayout
{
	/// <summary>The identifier of the always-present base policy the auxiliary policies overlay.</summary>
	public const string StandardPolicyId = "standard";

	public const string ManifestFileName = "policy.yaml";
	public const string ManifestSchemaFileName = "policy.schema.json";

	private const string StandardDisplayName = "Standard";
	private const string WorkflowsDirectoryName = "workflows";
	private const string DataDirectoryName = "data";

	/// <summary>
	///     The Standard definition first, then one overlay definition per <c>policies/*/</c> subdirectory
	///     ordered by id. Each overlay layers its own <c>workflows/</c> and <c>data/</c> over the shared base.
	/// </summary>
	/// <exception cref="EnrolmentPolicyConfigurationException">A policy subdirectory has no <c>policy.yaml</c>, or its manifest is invalid.</exception>
	public static IReadOnlyList<EnrolmentPolicyDefinition> Discover(
		string workflowsDirectory, string dataDirectory, string policiesDirectory)
	{
		var baseSource = new DirectoryDataSource(workflowsDirectory, dataDirectory);
		var definitions = new List<EnrolmentPolicyDefinition> {
			new(new(StandardPolicyId), StandardDisplayName, baseSource),
		};

		if (!Directory.Exists(policiesDirectory)) {
			return definitions;
		}

		var schemaText = ReadManifestSchema(dataDirectory);
		var policyDirectories = Directory.EnumerateDirectories(policiesDirectory)
										 .OrderBy(static directory => Path.GetFileName(directory), StringComparer.Ordinal);
		foreach (var policyDirectory in policyDirectories) {
			definitions.Add(BuildDefinition(policyDirectory, schemaText, baseSource));
		}

		return definitions;
	}

	private static string ReadManifestSchema(string dataDirectory)
	{
		var schemaPath = Path.Combine(dataDirectory, ManifestSchemaFileName);
		try {
			return File.ReadAllText(schemaPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			throw new EnrolmentPolicyConfigurationException(
				$"Policy manifest schema '{schemaPath}' could not be read: {ex.Message}", ex);
		}
	}

	private static EnrolmentPolicyDefinition BuildDefinition(
		string policyDirectory,
		string schemaText,
		IEnrolmentDataSource baseSource)
	{
		try {
			var id = new EnrolmentPolicyId(Path.GetFileName(policyDirectory));
			var overlay = new OverlayEnrolmentDataSource(
				new DirectoryDataSource(
					Path.Combine(policyDirectory, WorkflowsDirectoryName),
					Path.Combine(policyDirectory, DataDirectoryName)),
				baseSource);
			return new(id, ReadDisplayName(policyDirectory, schemaText), overlay);
		}
		catch (EnrolmentPolicyConfigurationException) {
			throw;
		}
		catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or UnauthorizedAccessException) {
			throw new EnrolmentPolicyConfigurationException(
				$"Policy folder '{policyDirectory}' is invalid: {ex.Message}", ex);
		}
	}

	private static string ReadDisplayName(string policyDirectory, string schemaText)
	{
		var manifestPath = Path.Combine(policyDirectory, ManifestFileName);
		if (!File.Exists(manifestPath)) {
			throw new EnrolmentPolicyConfigurationException(
				$"Policy folder '{policyDirectory}' is missing its '{ManifestFileName}' manifest.");
		}

		JsonNode node;
		try {
			node = YamlConverter.ToJsonNode(File.ReadAllText(manifestPath));
		}
		catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException) {
			throw new EnrolmentPolicyConfigurationException(
				$"Policy manifest '{manifestPath}' is invalid: {ex.Message}", ex);
		}

		SchemaValidator.Validate(node, schemaText, errors => new EnrolmentPolicyConfigurationException(
			$"Policy manifest '{manifestPath}' failed schema validation: {errors}"));

		return node["display_name"]?.GetValue<string>()
			   ?? throw new EnrolmentPolicyConfigurationException(
				   $"Policy manifest '{manifestPath}' has no display_name.");
	}
}
