namespace EnrolmentRules.Engine;

/// <summary>
///     Stream-based access to the shipped enrolment data. Each call must return fresh streams so the
///     bootstrap code can dispose them after a single load without sharing file handles between phases.
/// </summary>
public interface IEnrolmentDataSource
{
	IReadOnlyList<WorkflowContent> OpenWorkflows();

	Stream OpenWorkflowSchema();

	Stream OpenCatalogue();

	Stream OpenCatalogueSchema();

	Stream OpenQualifications();

	Stream OpenQualificationsSchema();

	Stream OpenThresholds();

	Stream OpenThresholdsSchema();

	Stream OpenTransitionMatrix();
}
