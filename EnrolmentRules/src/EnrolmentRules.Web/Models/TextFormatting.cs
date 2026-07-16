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
		QualificationType.Gcse => "GCSE",
		QualificationType.ALevel => "A Level",
		QualificationType.BtecExtendedCertificate => "BTEC Extended Certificate",
		QualificationType.BtecDiploma => "BTEC Diploma",
		QualificationType.Nvq => "NVQ",
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown qualification type."),
	};
}
