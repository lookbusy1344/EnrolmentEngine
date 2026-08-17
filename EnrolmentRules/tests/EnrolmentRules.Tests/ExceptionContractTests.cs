namespace EnrolmentRules.Tests;

using System.Reflection;
using System.Xml.Linq;
using AwesomeAssertions;
using Domain;
using Extensions.DependencyInjection;
using Prediction;
using Web.Configuration;

/// <summary>
///     F6 — every concrete exported <c>*Exception</c> type across the production library assemblies
///     exposes the standard public triad (<c>()</c>, <c>(string)</c>, <c>(string, Exception)</c>), so a
///     host can always construct or rethrow one without knowing its specialised constructors.
/// </summary>
public sealed class ExceptionContractTests
{
	[Fact]
	public void every_concrete_exported_exception_type_has_the_standard_public_triad()
	{
		var assemblies = new[] {
			typeof(StudentInput).Assembly, typeof(GradePredictor).Assembly, typeof(IEnrolmentEngine).Assembly,
			typeof(ServiceCollectionExtensions).Assembly, typeof(EnrolmentWebConfigurationException).Assembly,
		};

		var exceptionTypes = assemblies
							 .SelectMany(static assembly => assembly.GetExportedTypes())
							 .Where(static type => !type.IsAbstract && typeof(Exception).IsAssignableFrom(type))
							 .ToArray();

		exceptionTypes.Should().NotBeEmpty("this test should exercise real exception types, not vacuously pass");

		var missing = exceptionTypes
					 .Where(type => !HasStandardTriad(type))
					 .Select(static type => type.FullName)
					 .ToArray();

		missing.Should().BeEmpty(
			$"every concrete exported exception must expose public (), (string), and (string, Exception) constructors:{Environment.NewLine}"
			+ string.Join(Environment.NewLine, missing));
	}

	private static bool HasStandardTriad(Type type) =>
		HasPublicConstructor(type, []) && HasPublicConstructor(type, [typeof(string)])
										&& HasPublicConstructor(type, [typeof(string), typeof(Exception)]);

	private static bool HasPublicConstructor(Type type, Type[] parameterTypes) =>
		type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, parameterTypes, null) is not null;

	[Fact]
	public void every_student_boundary_documents_its_null_contract_in_generated_xml()
	{
		var document = XDocument.Load(Path.ChangeExtension(typeof(IEnrolmentEngine).Assembly.Location, ".xml"));
		var members = document.Descendants("member").ToDictionary(static member => (string)member.Attribute("name")!);
		var methods = new[] { typeof(IEnrolmentEvaluator), typeof(IEnrolmentAdvisor), typeof(IEnrolmentPolicyRegistry) }
			.SelectMany(static type => type.GetMethods())
			.Where(static method => method.GetParameters().Any(static parameter => parameter.Name == "student"))
			.Where(static method => !method.Name.EndsWith("Validated", StringComparison.Ordinal));

		var missing = methods
			.Where(method => !DocumentsException(members, method, typeof(ArgumentNullException)))
			.Select(static method => DocumentationId(method))
			.ToArray();

		missing.Should().BeEmpty(
			$"every public student boundary must document null as a programmer error:{Environment.NewLine}" +
			string.Join(Environment.NewLine, missing));
	}

	[Fact]
	public void policy_registry_constructor_documents_its_null_contract_in_generated_xml()
	{
		var document = XDocument.Load(Path.ChangeExtension(typeof(IEnrolmentEngine).Assembly.Location, ".xml"));
		var members = document.Descendants("member").ToDictionary(static member => (string)member.Attribute("name")!);
		var constructor = typeof(EnrolmentPolicyRegistry).GetConstructors().Single();

		DocumentsException(members, constructor, typeof(ArgumentNullException)).Should().BeTrue();
	}

	private static bool DocumentsException(
		Dictionary<string, XElement> members,
		MethodBase method,
		Type exceptionType) =>
		members.TryGetValue(DocumentationId(method), out var member)
		&& member.Elements("exception").Any(element => (string?)element.Attribute("cref") == $"T:{exceptionType.FullName}");

	private static string DocumentationId(MethodBase method)
	{
		var declaringType = method.DeclaringType!.FullName!.Replace('+', '.');
		var memberName = method.IsConstructor ? "#ctor" : method.Name;
		var parameters = string.Join(",", method.GetParameters().Select(static parameter => DocumentationTypeName(parameter.ParameterType)));
		return $"M:{declaringType}.{memberName}({parameters})";
	}

	private static string DocumentationTypeName(Type type)
	{
		if (type.IsByRef) {
			return DocumentationTypeName(type.GetElementType()!) + "@";
		}

		if (type.IsGenericType) {
			var definitionName = type.GetGenericTypeDefinition().FullName!;
			var unqualifiedName = definitionName[..definitionName.IndexOf('`')].Replace('+', '.');
			return $"{unqualifiedName}{{{string.Join(",", type.GetGenericArguments().Select(DocumentationTypeName))}}}";
		}

		return type.FullName!.Replace('+', '.');
	}
}
