namespace EnrolmentRules.Engine.Hosting;

/// <summary>
///     Layers an auxiliary policy's own workflows/catalogue/thresholds over a shared base source for
///     everything an auxiliary policy must not duplicate: the workflow/catalogue/threshold JSON schemas,
///     the qualification scale and its schema, and the DfE transition matrix. Not Elite-specific — any
///     auxiliary policy that owns only its rules-as-data and delegates shared machinery data uses this.
/// </summary>
/// <remarks>
///     Every member is a direct one-line delegation to whichever source owns that asset — there is no
///     caching or transformation here, so each call returns exactly the fresh stream the delegate itself
///     returns, and a failure (missing file, bad path) propagates unchanged with the delegate's own
///     exception type. <see cref="DirectoryDataSource" /> is unchanged and remains the base source for
///     existing single-policy consumers.
/// </remarks>
public sealed class OverlayEnrolmentDataSource : IEnrolmentDataSource
{
	private readonly IEnrolmentDataSource auxiliary;
	private readonly IEnrolmentDataSource @base;

	public OverlayEnrolmentDataSource(IEnrolmentDataSource auxiliary, IEnrolmentDataSource @base)
	{
		ArgumentNullException.ThrowIfNull(auxiliary);
		ArgumentNullException.ThrowIfNull(@base);
		this.auxiliary = auxiliary;
		this.@base = @base;
	}

	/// <summary>The auxiliary policy's own workflows.</summary>
	public IReadOnlyList<WorkflowContent> OpenWorkflows() => auxiliary.OpenWorkflows();

	/// <summary>The shared base workflow JSON schema.</summary>
	public Stream OpenWorkflowSchema() => @base.OpenWorkflowSchema();

	/// <summary>The auxiliary policy's own catalogue.</summary>
	public Stream OpenCatalogue() => auxiliary.OpenCatalogue();

	/// <summary>The shared base catalogue JSON schema.</summary>
	public Stream OpenCatalogueSchema() => @base.OpenCatalogueSchema();

	/// <summary>The shared base qualification scale — not duplicated per policy.</summary>
	public Stream OpenQualifications() => @base.OpenQualifications();

	/// <summary>The shared base qualification scale JSON schema.</summary>
	public Stream OpenQualificationsSchema() => @base.OpenQualificationsSchema();

	/// <summary>The auxiliary policy's own thresholds.</summary>
	public Stream OpenThresholds() => auxiliary.OpenThresholds();

	/// <summary>The shared base thresholds JSON schema.</summary>
	public Stream OpenThresholdsSchema() => @base.OpenThresholdsSchema();

	/// <summary>The shared base DfE transition matrix — statistical data, not policy, so not duplicated.</summary>
	public Stream OpenTransitionMatrix() => @base.OpenTransitionMatrix();
}
