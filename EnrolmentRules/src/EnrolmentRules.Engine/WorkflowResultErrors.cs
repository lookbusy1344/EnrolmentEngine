namespace EnrolmentRules.Engine;

using RulesEngine.Models;

internal static class WorkflowResultErrors
{
	internal static IEnumerable<string> Find(IEnumerable<RuleResultTree> results) =>
		results.SelectMany(Flatten)
			   .Where(static result => !string.IsNullOrWhiteSpace(result.ExceptionMessage))
			   .Select(static result => $"{result.Rule.RuleName}: {result.ExceptionMessage}");

	private static IEnumerable<RuleResultTree> Flatten(RuleResultTree result)
	{
		yield return result;
		if (result.ChildResults is not null) {
			foreach (var child in result.ChildResults.SelectMany(Flatten)) {
				yield return child;
			}
		}
	}
}
