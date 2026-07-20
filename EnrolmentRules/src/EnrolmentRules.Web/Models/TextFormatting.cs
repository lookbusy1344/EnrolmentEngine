namespace EnrolmentRules.Web.Models;

using Domain;

/// <summary>
///     Display-only formatting for the engine's snake_case subject/hobby keys and <see cref="QualificationType" />
///     enum names. Never changes the underlying value posted back to a handler — only what a label shows.
/// </summary>
public static class TextFormatting
{
	/// <summary>Turn a snake_case catalogue/GCSE/hobby key into a title-cased label, e.g. "english_language" → "English Language".</summary>
	public static string Prettify(string key)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (key.Length == 0) {
			return key;
		}

		var words = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
		return string.Join(' ', words.Select(static word => char.ToUpperInvariant(word[0]) + word[1..]));
	}

	/// <summary>A friendly label for a <see cref="QualificationType" />, e.g. <c>BtecDiploma</c> → "BTEC Diploma".</summary>
	public static string Label(QualificationType type) => type switch {
		QualificationType.ALevel => "A Level",
		QualificationType.BtecExtendedCertificate => "BTEC Extended Certificate",
		QualificationType.BtecDiploma => "BTEC Diploma",
		QualificationType.Nvq => "NVQ",
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown qualification type."),
	};

	/// <summary>
	///     A friendly label for one grade token within <paramref name="type" />'s scale, e.g.
	///     <c>(ALevel, "a_star")</c> → "A*", <c>(BtecDiploma, "distinction_star")</c> → "Distinction*",
	///     <c>(Nvq, "level_1")</c> → "Level 1". Cosmetic only — never changes the token posted back.
	/// </summary>
	public static string GradeLabel(QualificationType type, string grade)
	{
		ArgumentNullException.ThrowIfNull(grade);
		const string StarSuffix = "_star";
		return type switch {
			QualificationType.ALevel => grade == "a_star" ? "A*" : grade.ToUpperInvariant(),
			QualificationType.BtecExtendedCertificate or QualificationType.BtecDiploma => grade.EndsWith(StarSuffix, StringComparison.Ordinal)
				? Prettify(grade[..^StarSuffix.Length]) + "*"
				: Prettify(grade),
			QualificationType.Nvq => Prettify(grade),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown qualification type."),
		};
	}
}
