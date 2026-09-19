namespace EnrolmentRules.Cli;

using System.Globalization;
using Domain;

/// <summary>
///     Renders an <see cref="ExplainedResult" /> as plain Markdown prose. This is a pure projection of
///     the already-evaluated explanation object; it does not consult the engine or re-run any rules.
/// </summary>
public static class ExplanationRenderer
{
	public static void Render(ExplainedResult result, TextWriter writer)
	{
		writer.WriteLine(result.Eligible ? "# Eligible" : "# Ineligible");
		if (!result.Eligible) {
			foreach (var reason in result.EligibilityReasons) {
				writer.WriteLine($"- {Escape(reason)}");
			}

			writer.WriteLine();
		}

		foreach (var explanation in result.Explanations) {
			RenderExplanation(explanation, writer);
			writer.WriteLine();
		}

		var summary = result.Summary;
		writer.WriteLine(
			$"Summary: {summary.GreenCount} green, {summary.AmberCount} amber; programme priority score {summary.ProgrammePriorityScore.ToString("0.##", CultureInfo.InvariantCulture)}");
	}

	private static void RenderExplanation(Explanation explanation, TextWriter writer)
	{
		writer.WriteLine($"## {Escape(EnumNames.NameOf(explanation.Subject))}");
		writer.WriteLine($"Final rating: {EnumNames.NameOf(explanation.Rating)}. {Escape(explanation.Reason)}");

		var predictedGrade = ALevelGrade.NearestBand(explanation.PredictedPoints);
		writer.WriteLine(
			$"The engine rated this **{Escape(EnumNames.NameOf(explanation.BaseRating))}** because: {Escape(explanation.BaseReason)} (predicted {Escape(predictedGrade.Name)}, ~{explanation.PredictedPoints.ToString("0.##", CultureInfo.InvariantCulture)}).");

		if (explanation.EntryEquivalentReason is string entryEquivalentReason) {
			writer.WriteLine(Escape(entryEquivalentReason));
		}

		if (explanation.Overrides.Count == 0) {
			writer.WriteLine("This was not downgraded.");
			return;
		}

		writer.WriteLine("This was downgraded:");
		foreach (var override_ in explanation.Overrides) {
			writer.WriteLine($"- {EnumNames.NameOf(override_.From)} → {EnumNames.NameOf(override_.To)}: {Escape(override_.Reason)}");
		}
	}

	private static string Escape(string text) =>
		text
			.Replace(@"\", @"\\", StringComparison.Ordinal)
			.Replace("*", @"\*", StringComparison.Ordinal)
			.Replace("_", @"\_", StringComparison.Ordinal)
			.Replace("`", @"\`", StringComparison.Ordinal);
}
