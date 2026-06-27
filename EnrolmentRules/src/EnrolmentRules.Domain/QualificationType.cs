namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     The typed qualification vocabulary for <see cref="Qualification" />. The values are serialised as
///     snake_case strings so the student document can express GCSEs, A-levels, BTECs and NVQs in one
///     shape without a bespoke converter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<QualificationType>))]
public enum QualificationType
{
	[JsonStringEnumMemberName("gcse")] Gcse,

	[JsonStringEnumMemberName("a_level")] ALevel,

	[JsonStringEnumMemberName("btec_extended_certificate")]
	BtecExtendedCertificate,

	[JsonStringEnumMemberName("btec_diploma")]
	BtecDiploma,

	[JsonStringEnumMemberName("nvq")] Nvq,
}
