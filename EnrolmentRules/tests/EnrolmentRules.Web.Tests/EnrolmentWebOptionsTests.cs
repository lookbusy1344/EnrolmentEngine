namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Configuration;

public sealed class EnrolmentWebOptionsTests
{
	[Fact]
	public void Missing_configuration_defaults_to_razor()
	{
		var configuration = Build([]);

		var options = EnrolmentWebOptions.LoadAndValidate(configuration);

		options.DefaultExperience.Should().Be(ExperienceKind.Razor);
	}

	[Theory]
	[InlineData("Razor", ExperienceKind.Razor)]
	[InlineData("vue", ExperienceKind.Vue)]
	[InlineData("VUE", ExperienceKind.Vue)]
	public void Recognised_values_parse_case_insensitively(string configured, ExperienceKind expected)
	{
		var configuration = Build([new("EnrolmentWeb:DefaultExperience", configured)]);

		var options = EnrolmentWebOptions.LoadAndValidate(configuration);

		options.DefaultExperience.Should().Be(expected);
	}

	[Fact]
	public void Unrecognised_value_throws()
	{
		var configuration = Build([new("EnrolmentWeb:DefaultExperience", "Angular")]);

		var act = () => EnrolmentWebOptions.LoadAndValidate(configuration);

		act.Should().Throw<EnrolmentWebConfigurationException>().WithMessage("*Angular*");
	}

	private static IConfiguration Build(IEnumerable<KeyValuePair<string, string?>> values) =>
		new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
