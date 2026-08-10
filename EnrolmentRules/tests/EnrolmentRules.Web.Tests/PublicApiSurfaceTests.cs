namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Pages;
using Services;

public sealed class PublicApiSurfaceTests
{
	[Fact]
	public void Web_assembly_does_not_export_types_in_foreign_namespaces()
	{
		var foreignTypes = typeof(Program).Assembly
			.GetExportedTypes()
			.Where(static type => type.Namespace is null || !type.Namespace.StartsWith("EnrolmentRules.Web", StringComparison.Ordinal))
			.Select(static type => type.FullName)
			.Order(StringComparer.Ordinal)
			.ToArray();

		foreignTypes.Should().BeEmpty("the web app must not publish implementation helpers under unrelated namespaces");
	}

	[Fact]
	public void Razor_model_exposes_type_keyed_prior_qualification_subject_groups()
	{
		var property = typeof(RazorModel).GetProperty(nameof(RazorModel.PriorQualificationSubjectGroups));

		property.Should().NotBeNull();
		property!.PropertyType.Should().BeAssignableTo<IReadOnlyList<SubjectOptionGroup>>();
	}
}
