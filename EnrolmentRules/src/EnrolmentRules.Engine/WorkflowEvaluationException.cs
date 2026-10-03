namespace EnrolmentRules.Engine;

/// <summary>A workflow expression failed while evaluating a student's facts.</summary>
public sealed class WorkflowEvaluationException : WorkflowException
{
	public WorkflowEvaluationException() { }

	public WorkflowEvaluationException(string message) : base(message) { }

	public WorkflowEvaluationException(string message, Exception innerException) : base(message, innerException) { }

	public WorkflowEvaluationException(string workflowName, string errors)
		: base($"Workflow '{workflowName}' failed evaluation: {errors}") { }
}
