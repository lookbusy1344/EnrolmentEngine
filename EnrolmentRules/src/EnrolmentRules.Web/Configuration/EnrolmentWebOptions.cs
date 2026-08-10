namespace EnrolmentRules.Web.Configuration;

/// <summary>
///     The <c>EnrolmentWeb</c> configuration section, loaded and validated once at startup (see
///     <see cref="LoadAndValidate" />) rather than resolved lazily through <c>IOptions&lt;T&gt;</c>, matching
///     the fail-fast load pattern the domain data stores already use for <c>data/*.yaml</c>.
/// </summary>
public sealed record EnrolmentWebOptions(ExperienceKind DefaultExperience)
{
	private const string SectionKey = "EnrolmentWeb";
	private const string DefaultExperienceKey = "DefaultExperience";

	/// <summary>Reads <c>EnrolmentWeb:DefaultExperience</c>, defaulting to <see cref="ExperienceKind.Razor" /> when unset.</summary>
	/// <exception cref="EnrolmentWebConfigurationException">
	///     <c>EnrolmentWeb:DefaultExperience</c> is present but not one of <see cref="ExperienceKind" />'s names.
	/// </exception>
	public static EnrolmentWebOptions LoadAndValidate(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var raw = configuration[$"{SectionKey}:{DefaultExperienceKey}"];
		if (raw is null) {
			return new(ExperienceKind.Razor);
		}

		if (!Enum.TryParse<ExperienceKind>(raw, true, out var kind) || !Enum.IsDefined(kind)) {
			var allowed = string.Join(", ", Enum.GetNames<ExperienceKind>());
			throw new EnrolmentWebConfigurationException(
				$"Invalid {SectionKey}:{DefaultExperienceKey} value '{raw}'. Expected one of: {allowed}.");
		}

		return new(kind);
	}
}
