namespace EnrolmentRules.Web.Api;

using Domain;
using Engine;
using Models;
using Services;

/// <summary>Builds an <see cref="EnrolmentOptionsResponse" /> from the same <see cref="EnrolmentOptionsService" /> the Razor page uses.</summary>
public static class EnrolmentOptionsResponseFactory
{
	public static EnrolmentOptionsResponse Create(EnrolmentOptionsService options, IReadOnlyList<EnrolmentPolicyDescriptor> availablePolicies)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(availablePolicies);
		return new(
			ToPolicyDescriptor(options.Policy.Descriptor),
			[.. availablePolicies.Select(ToPolicyDescriptor)],
			options.DefaultDateOfBirth(),
			options.DefaultAge(),
			[.. options.GcseSubjectOptions.Select(ToLabelledOption)],
			[.. options.ALevelSubjects.Select(static subject => new OptionItem(subject.Value, TextFormatting.Prettify(subject.Value)))],
			[.. options.PriorQualificationSubjectGroups.Select(ToSubjectGroup)],
			[.. options.QualificationTypeOptions.Select(type => ToGradeOptions(type, options.QualificationGradeOptions[type]))],
			[.. options.HobbyOptions.Select(ToLabelledOption)],
			options.MinChoices,
			options.ChoiceLimit);
	}

	private static PolicyDescriptorResponse ToPolicyDescriptor(EnrolmentPolicyDescriptor descriptor) =>
		new(descriptor.Id.Value, descriptor.DisplayName);

	private static OptionItem ToLabelledOption(string key) => new(key, TextFormatting.Prettify(key));

	private static QualificationSubjectGroup ToSubjectGroup(SubjectOptionGroup group) =>
		new(group.Type.ToString(), group.Label, [.. group.Subjects.Select(ToLabelledOption)]);

	private static QualificationGradeOptions ToGradeOptions(QualificationType type, IReadOnlyList<string> grades) =>
		new(type.ToString(), [.. grades.Select(grade => new OptionItem(grade, TextFormatting.GradeLabel(type, grade)))]);
}
