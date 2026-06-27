namespace EnrolmentRules.Domain;

/// <summary>
///     Severity for static workflow lint findings. Errors indicate a structural problem that can
///     mis-enrol students; warnings are reserved for non-blocking authoring issues.
/// </summary>
public enum LintSeverity
{
	Warning,
	Error,
}

/// <summary>
///     A structural finding from the workflow linter: which workflow and rule it concerns, how severe
///     it is, and a human-readable explanation.
/// </summary>
public sealed record LintFinding(string Workflow, string? Rule, LintSeverity Severity, string Message);
