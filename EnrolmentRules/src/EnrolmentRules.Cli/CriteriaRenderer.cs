namespace EnrolmentRules.Cli;

using Domain;

/// <summary>
///     Renders a <see cref="SubjectCriteria" /> as plain Markdown a student can read. A pure projection
///     of the already-composed criteria object — it consults no rules of its own, so what it prints and
///     what the engine evaluates cannot diverge.
/// </summary>
public static class CriteriaRenderer
{
	public static void Render(SubjectCriteria criteria, TextWriter writer)
	{
		ArgumentNullException.ThrowIfNull(criteria);
		ArgumentNullException.ThrowIfNull(writer);

		var subject = Title(criteria.Subject);
		writer.WriteLine($"# What you need for A-level {subject}");
		writer.WriteLine();

		writer.WriteLine("## What the colours mean");
		foreach (var meaning in RatingMeaning.All) {
			writer.WriteLine($"- **{Title(EnumNames.NameOf(meaning.Rating))}** — {meaning.Meaning}");
		}

		Section(writer, "Everyone needs these first", criteria.Eligibility);
		Section(writer, $"To get a green light in {subject}, you also need", criteria.Green);
		Section(writer, $"To get at least an amber light in {subject}, you need", criteria.Amber);
		Section(writer, "If you already have other qualifications", criteria.PriorQualifications);
		Section(writer, $"Other things that can affect {subject}", criteria.Downgrades);
	}

	private static void Section(TextWriter writer, string heading, EquatableArray<string> bullets)
	{
		if (bullets.Count == 0) {
			return;
		}

		writer.WriteLine();
		writer.WriteLine($"## {heading}");
		foreach (var bullet in bullets) {
			writer.WriteLine($"- {bullet}");
		}
	}

	/// <summary>Title-case a snake_case rule name for display (<c>further_maths</c> ⇒ <c>Further Maths</c>).</summary>
	private static string Title(Subject subject) => Title(EnumNames.NameOf(subject));

	private static string Title(string snakeCase) =>
		string.Join(' ', snakeCase
			.Split('_', StringSplitOptions.RemoveEmptyEntries)
			.Select(static word => string.Concat(char.ToUpperInvariant(word[0]).ToString(), word[1..])));
}
