namespace EnrolmentRules.Tests;

using AwesomeAssertions;

/// <summary>
///     Elite auxiliary policy plan, step 2.1 — <see cref="EnrolmentPolicyId" /> and
///     <see cref="EnrolmentPolicyRegistry" />: the library's single source of truth for policy identity,
///     display labels, the default policy and immutable engine instances. Selection is always an explicit
///     per-call lookup — the registry itself never holds a mutable "current policy".
/// </summary>
public sealed class EnrolmentPolicyRegistryTests
{
	private static DirectoryDataSource StandardSource() => new(Harness.WorkflowsDir, Harness.DataDir);

	private static EnrolmentPolicyDefinition StandardDefinition(string id = "standard", string name = "Standard") =>
		new(new(id), name, StandardSource());

	// --- EnrolmentPolicyId ---

	[Theory]
	[InlineData("standard")]
	[InlineData("elite")]
	[InlineData("a")]
	[InlineData("a-b-9")]
	public void valid_identifiers_parse(string value)
	{
		EnrolmentPolicyId.TryParse(value, out var id).Should().BeTrue();
		id.Value.Should().Be(value);
	}

	[Theory]
	[InlineData("")]
	[InlineData("Elite")]
	[InlineData("elite_auxiliary")]
	[InlineData("1elite")]
	[InlineData("-elite")]
	[InlineData(" elite")]
	public void invalid_identifiers_are_rejected(string value)
	{
		EnrolmentPolicyId.TryParse(value, out _).Should().BeFalse();
		var act = () => new EnrolmentPolicyId(value);
		act.Should().Throw<ArgumentException>();
	}

	// --- EnrolmentPolicyRegistry construction ---

	[Fact]
	public void a_single_definition_builds_eagerly_and_becomes_the_default()
	{
		var registry = new EnrolmentPolicyRegistry([StandardDefinition()], new("standard"), static () => Harness.AsOf);

		registry.DefaultPolicyId.Should().Be(new("standard"));
		registry.Descriptors.Should().ContainSingle(d => d.Id == new EnrolmentPolicyId("standard") && d.DisplayName == "Standard");

		var policy = registry.GetPolicy(new("standard"));
		policy.Engine.Evaluate(new("S", new Dictionary<string, int> {
			["maths"] = 8,
		}, [])).Should().NotBeNull();
	}

	[Fact]
	public void descriptor_order_matches_registration_order()
	{
		var registry = new EnrolmentPolicyRegistry(
			[StandardDefinition(), StandardDefinition("elite", "Elite")],
			new("standard"),
			static () => Harness.AsOf);

		registry.Descriptors.Select(d => d.Id.Value).Should().Equal("standard", "elite");
	}

	[Fact]
	public void descriptors_cannot_be_mutated_through_the_exposed_collection()
	{
		var registry = new EnrolmentPolicyRegistry(
			[StandardDefinition(), StandardDefinition("elite", "Elite")],
			new("standard"),
			static () => Harness.AsOf);
		var collection = registry.Descriptors.Should().BeAssignableTo<ICollection<EnrolmentPolicyDescriptor>>().Subject;

		collection.IsReadOnly.Should().BeTrue();
		var act = () => collection.Clear();

		act.Should().Throw<NotSupportedException>();
		registry.Descriptors.Select(static descriptor => descriptor.Id.Value).Should().Equal("standard", "elite");
	}

	[Fact]
	public void get_policy_throws_a_typed_exception_for_an_unknown_identifier()
	{
		var registry = new EnrolmentPolicyRegistry([StandardDefinition()], new("standard"), static () => Harness.AsOf);

		var act = () => registry.GetPolicy(new("elite"));

		act.Should().Throw<UnknownEnrolmentPolicyException>().WithMessage("*elite*");
	}

	[Fact]
	public void try_get_policy_returns_false_for_an_unknown_identifier_without_throwing()
	{
		var registry = new EnrolmentPolicyRegistry([StandardDefinition()], new("standard"), static () => Harness.AsOf);

		registry.TryGetPolicy(new("elite"), out var policy).Should().BeFalse();
		policy.Should().BeNull();
	}

	[Fact]
	public void construction_rejects_an_empty_definition_list()
	{
		var act = () => new EnrolmentPolicyRegistry([], new("standard"), static () => Harness.AsOf);

		act.Should().Throw<EnrolmentPolicyConfigurationException>();
	}

	[Fact]
	public void construction_rejects_a_duplicate_identifier()
	{
		var act = () => new EnrolmentPolicyRegistry(
			[StandardDefinition(), StandardDefinition("standard", "Standard Two")],
			new("standard"),
			static () => Harness.AsOf);

		act.Should().Throw<EnrolmentPolicyConfigurationException>().WithMessage("*standard*");
	}

	[Fact]
	public void construction_rejects_a_duplicate_display_name()
	{
		var act = () => new EnrolmentPolicyRegistry(
			[StandardDefinition(), StandardDefinition("elite")],
			new("standard"),
			static () => Harness.AsOf);

		act.Should().Throw<EnrolmentPolicyConfigurationException>().WithMessage("*display name*");
	}

	[Fact]
	public void constructing_a_definition_rejects_a_blank_display_name()
	{
		var act = static () => new EnrolmentPolicyDefinition(new("standard"), " ", StandardSource());

		act.Should().Throw<ArgumentException>().WithParameterName("displayName");
	}

	[Fact]
	public void construction_rejects_a_null_definition_entry()
	{
		var act = () => new EnrolmentPolicyRegistry(
			[StandardDefinition(), null!], new("standard"), static () => Harness.AsOf);

		act.Should().Throw<EnrolmentPolicyConfigurationException>();
	}

	[Fact]
	public void construction_rejects_an_unknown_default()
	{
		var act = () => new EnrolmentPolicyRegistry([StandardDefinition()], new("elite"), static () => Harness.AsOf);

		act.Should().Throw<EnrolmentPolicyConfigurationException>().WithMessage("*elite*");
	}

	[Fact]
	public void a_broken_definition_fails_construction_with_the_policy_id_named()
	{
		var brokenDirectory = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "broken-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(brokenDirectory);
		var broken = new EnrolmentPolicyDefinition(new("broken"), "Broken", new DirectoryDataSource(brokenDirectory, brokenDirectory));

		var act = () => new EnrolmentPolicyRegistry([StandardDefinition(), broken], new("standard"), static () => Harness.AsOf);

		act.Should().Throw<EnrolmentPolicyBuildException>()
		   .Which.PolicyId.Should().Be(new("broken"));
	}

	[Fact]
	public void two_registered_policies_evaluate_independently()
	{
		// Both definitions point at the same shipped Standard data here (Elite assets are step 3.1), but
		// this proves the registry carries two independently constructed engines under two identifiers
		// rather than one shared instance.
		var registry = new EnrolmentPolicyRegistry(
			[StandardDefinition(), StandardDefinition("elite", "Elite")],
			new("standard"),
			static () => Harness.AsOf);

		var standard = registry.GetPolicy(new("standard"));
		var elite = registry.GetPolicy(new("elite"));

		standard.Engine.Should().NotBeSameAs(elite.Engine);
		standard.Descriptor.DisplayName.Should().Be("Standard");
		elite.Descriptor.DisplayName.Should().Be("Elite");
	}
}
