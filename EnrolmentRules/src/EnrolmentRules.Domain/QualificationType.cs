namespace EnrolmentRules.Domain;

using System.Text.Json.Serialization;

/// <summary>
///     The typed "other qualification" vocabulary for <see cref="Qualification" />. GCSEs are captured by
///     the dedicated GCSE section (<see cref="GcseResult" />) and are deliberately not a member here. The
///     values are serialised as snake_case strings so the student document can express A-levels, BTECs and
///     NVQs in one shape without a bespoke converter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<QualificationType>))]
public enum QualificationType
{
	[JsonStringEnumMemberName("a_level")] ALevel,

	[JsonStringEnumMemberName("btec_extended_certificate")]
	BtecExtendedCertificate,

	[JsonStringEnumMemberName("btec_diploma")]
	BtecDiploma,

	[JsonStringEnumMemberName("nvq")] Nvq,
}
