namespace EnrolmentRules.Tests;

using System.Reflection;
using AwesomeAssertions;
using Domain;
using Extensions.DependencyInjection;
using Prediction;

public sealed class PublicApiSurfaceTests
{
	private const string RulesEngineAssemblyName = "RulesEngine";
	private const string RulesEngineNamespace = "RulesEngine";
	private const string EngineAssemblyName = "EnrolmentRules.Engine";

	[Fact]
	public void only_engine_assembly_references_rules_engine()
	{
		var references = ValueTypeSizeGuard.ProductionAssemblyNames
			.Where(static assemblyName => assemblyName != EngineAssemblyName)
			.Select(Assembly.Load)
			.Where(static assembly => assembly.GetReferencedAssemblies()
				.Any(reference => reference.Name == RulesEngineAssemblyName))
			.Select(static assembly => assembly.GetName().Name)
			.ToArray();

		references.Should().BeEmpty("RulesEngine is an implementation detail of EnrolmentRules.Engine");
	}

	[Fact]
	public void engine_public_signatures_do_not_expose_rules_engine_types()
	{
		var leaks = typeof(IEnrolmentEngine).Assembly
			.GetExportedTypes()
			.SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
			.SelectMany(MemberSignatureTypes)
			.Where(IsRulesEngineType)
			.Select(static type => type.FullName)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();

		leaks.Should().BeEmpty("RulesEngine types must remain behind the Engine public boundary");
	}

	[Fact]
	public void equatable_collections_expose_no_implicit_conversion_operators()
	{
		var wrapperTypes = new[] {
			typeof(EquatableArray<int>), typeof(EquatableDictionary<string, int>),
		};

		var implicitOperators = wrapperTypes
								.SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
								.Where(static method => method.Name == "op_Implicit")
								.Select(static method => method.ToString())
								.ToArray();

		implicitOperators.Should().BeEmpty("collection copying must remain explicit at the call site");
	}

	[Fact]
	public void public_surface_matches_the_design_spec()
	{
		var assembliesByLabel = new Dictionary<string, Assembly> {
			["EnrolmentRules.Domain"] = typeof(StudentInput).Assembly,
			["EnrolmentRules.Prediction"] = typeof(GradePredictor).Assembly,
			["EnrolmentRules.Engine"] = typeof(IEnrolmentEngine).Assembly,
			["EnrolmentRules.Extensions.DependencyInjection"] = typeof(ServiceCollectionExtensions).Assembly,
		};

		var specPath = Path.Combine(Harness.RepoRoot, "docs", "design", "2026-07-03-framework-design-guidelines-api-spec.md");
		var spec = ApiSpecInventory.Parse(File.ReadAllLines(specPath));

		spec.DuplicateTypes.Should().BeEmpty("no type may be listed twice in the specification inventory");
		spec.UnknownLabels.Should().BeEmpty("every inventory block label must match a known assembly");

		var seenLabels = spec.TypesByLabel.Keys.ToArray();
		var missingLabels = assembliesByLabel.Keys.Except(seenLabels, StringComparer.Ordinal).ToArray();
		missingLabels.Should().BeEmpty("every known assembly must have an inventory block in the specification");

		foreach (var (label, assembly) in assembliesByLabel) {
			var actual = assembly.GetExportedTypes()
								 .Select(static type => type.FullName)
								 .OrderBy(static name => name, StringComparer.Ordinal)
								 .ToArray();

			var expectedSorted = spec.TypesByLabel[label].OrderBy(static name => name, StringComparer.Ordinal).ToArray();
			var missing = expectedSorted.Except(actual, StringComparer.Ordinal).ToArray();
			var extra = actual.Except(expectedSorted, StringComparer.Ordinal).ToArray();

			var parts = new List<string>(2);
			if (missing.Length > 0) {
				parts.Add($"Missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
			}

			if (extra.Length > 0) {
				parts.Add($"Extra:{Environment.NewLine}{string.Join(Environment.NewLine, extra)}");
			}

			(missing.Length, extra.Length).Should().Be((0, 0), $"{label}:{Environment.NewLine}{string.Join(Environment.NewLine, parts)}");
		}
	}

	private static IEnumerable<Type> MemberSignatureTypes(MemberInfo member) =>
		member switch {
			MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(static parameter => parameter.ParameterType)],
			ConstructorInfo constructor => constructor.GetParameters().Select(static parameter => parameter.ParameterType),
			PropertyInfo property => [property.PropertyType],
			FieldInfo field => [field.FieldType],
			EventInfo eventInfo when eventInfo.EventHandlerType is not null => [eventInfo.EventHandlerType],
			_ => [],
		};

	private static bool IsRulesEngineType(Type type)
	{
		if (type.IsByRef || type.IsArray || type.IsPointer) {
			return IsRulesEngineType(type.GetElementType()!);
		}

		if (type.Namespace is not null
			&& (type.Namespace == RulesEngineNamespace
				|| type.Namespace.StartsWith(RulesEngineNamespace + '.', StringComparison.Ordinal))) {
			return true;
		}

		return type.IsGenericType && type.GetGenericArguments().Any(IsRulesEngineType);
	}
}

/// <summary>
///     Parses the "Authoritative inventory" fenced code blocks (```types:&lt;assembly&gt; ... ```) out of the API
///     specification markdown, so <see cref="PublicApiSurfaceTests" /> has one source of truth instead of a
///     second, hand-maintained allow-list.
/// </summary>
internal static class ApiSpecInventory
{
	private const string BlockPrefix = "```types:";

	public static (
		IReadOnlyDictionary<string, IReadOnlyList<string>> TypesByLabel,
		IReadOnlyList<string> DuplicateTypes,
		IReadOnlyList<string> UnknownLabels) Parse(IReadOnlyList<string> lines)
	{
		var knownLabels = new HashSet<string>(StringComparer.Ordinal) {
			"EnrolmentRules.Domain", "EnrolmentRules.Prediction", "EnrolmentRules.Engine", "EnrolmentRules.Extensions.DependencyInjection",
		};

		var typesByLabel = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		var seenTypes = new HashSet<string>(StringComparer.Ordinal);
		var duplicateTypes = new List<string>();
		var unknownLabels = new List<string>();

		var index = 0;
		while (index < lines.Count) {
			var line = lines[index];
			if (!line.StartsWith(BlockPrefix, StringComparison.Ordinal)) {
				++index;
				continue;
			}

			var label = line[BlockPrefix.Length..].Trim();
			if (!knownLabels.Contains(label)) {
				unknownLabels.Add(label);
			}

			var blockTypes = new List<string>();
			++index;
			while (index < lines.Count && lines[index] != "```") {
				var typeName = lines[index].Trim();
				if (typeName.Length > 0) {
					if (!seenTypes.Add(typeName)) {
						duplicateTypes.Add(typeName);
					}

					blockTypes.Add(typeName);
				}

				++index;
			}

			typesByLabel[label] = blockTypes;
			++index;
		}

		return (typesByLabel, duplicateTypes, unknownLabels);
	}
}
