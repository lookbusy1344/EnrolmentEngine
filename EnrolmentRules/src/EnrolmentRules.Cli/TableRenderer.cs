namespace EnrolmentRules.Cli;

using System.Globalization;
using Domain;
using Spectre.Console;

/// <summary>
///     Renders an <see cref="EnrolmentResult" /> as the human-facing coloured traffic-light table
///     (<c>--table</c>). Colour is a terminal capability, not a hardcoded assumption: the
///     <see cref="IAnsiConsole" /> is built over the supplied writer, so a real TTY gets green/amber/red
///     and a captured <see cref="StringWriter" /> (tests, pipes) gets the same table as plain text.
/// </summary>
internal static class TableRenderer
{
	public static void Render(EnrolmentResult result, TextWriter stdout)
	{
		var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(stdout) });

		if (!result.Eligible) {
			console.MarkupLine("[red]Ineligible[/] — every subject is red:");
			foreach (var reason in result.EligibilityReasons) {
				console.MarkupLine($"  • {Markup.Escape(reason)}");
			}
		}

		var table = new Table()
			.Border(TableBorder.Rounded)
			.AddColumn("Subject")
			.AddColumn("Rating")
			.AddColumn("Reason");

		foreach (var recommendation in result.Recommendations) {
			_ = table.AddRow(
				new Markup(Markup.Escape(EnumNames.NameOf(recommendation.Subject))),
				RatingCell(recommendation.Rating),
				new Markup(Markup.Escape(recommendation.Reason)));
		}

		console.Write(table);

		var summary = result.Summary;
		console.MarkupLine(string.Create(
			CultureInfo.InvariantCulture,
			$"[green]{summary.GreenCount} green[/]  [yellow]{summary.AmberCount} amber[/]  programme priority score {summary.ProgrammePriorityScore:0.##}"));
	}

	private static Markup RatingCell(Rating rating)
	{
		var colour = rating switch {
			Rating.Green => "green",
			Rating.Amber => "yellow",
			Rating.Red => "red",
			_ => "default",
		};
		return new($"[{colour}]{EnumNames.NameOf(rating)}[/]");
	}
}
