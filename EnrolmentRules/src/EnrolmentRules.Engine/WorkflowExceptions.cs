namespace EnrolmentRules.Engine;

/// <summary>Base type for workflow problems detected at startup (fail loud, never silent — Reservation 1).</summary>
public abstract class WorkflowException : Exception
{
	protected WorkflowException() { }

	protected WorkflowException(string message) : base(message) { }

	protected WorkflowException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>A workflow file failed JSON-Schema (structural) validation at load time.</summary>
public sealed class WorkflowSchemaException : WorkflowException
{
	public WorkflowSchemaException() { }

	public WorkflowSchemaException(string message) : base(message) { }

	public WorkflowSchemaException(string message, Exception innerException) : base(message, innerException) { }

	public WorkflowSchemaException(string file, string errors)
		: base($"Workflow file '{file}' failed schema validation: {errors}")
	{
	}
}

/// <summary>
///     A schema-valid workflow whose lambda expression failed to compile/bind against the canonical
///     probe input. This is the real boot-time guard for lambda semantics (typo'd field, bad expression).
/// </summary>
public sealed class WorkflowProbeException : WorkflowException
{
	public WorkflowProbeException() { }

	public WorkflowProbeException(string message) : base(message) { }

	public WorkflowProbeException(string message, Exception innerException) : base(message, innerException) { }

	public WorkflowProbeException(string workflowName, string errors)
		: base($"Workflow '{workflowName}' failed probe-evaluation at startup: {errors}")
	{
	}

	public WorkflowProbeException(string workflowName, string errors, Exception innerException)
		: base($"Workflow '{workflowName}' failed probe-evaluation at startup: {errors}", innerException)
	{
	}
}
