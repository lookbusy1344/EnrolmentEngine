namespace EnrolmentRules.Web.Api;

using Models;
using Services;

/// <summary>Builds an <see cref="EnrolmentOptionsResponse" /> from the same <see cref="EnrolmentOptionsService" /> the Razor page uses.</summary>
public static class EnrolmentOptionsResponseFactory
{
	public static EnrolmentOptionsResponse Create(EnrolmentOptionsService options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return new(
			options.DefaultDateOfBirth(),
			options.DefaultAge(),
			[.. options.GcseSubjectOptions.Select(ToLabelledOption)],
			[.. options.ALevelSubjects.Select(static subject => new OptionItem(subject.Value, TextFormatting.Prettify(subject.Value)))],
			[.. options.PriorQualificationSubjectOptions.Select(ToLabelledOption)],
			[.. options.QualificationTypeOptions.Select(static type => new OptionItem(type.ToString(), TextFormatting.Label(type)))],
			[.. options.HobbyOptions.Select(ToLabelledOption)],
			options.ChoiceLimit);
	}

	private static OptionItem ToLabelledOption(string key) => new(key, TextFormatting.Prettify(key));
}
