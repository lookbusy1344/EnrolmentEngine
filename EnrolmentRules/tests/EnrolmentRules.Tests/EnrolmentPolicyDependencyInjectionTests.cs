namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Elite auxiliary policy plan, step 2.4 — <see cref="ServiceCollectionExtensions.AddEnrolmentPolicies" />:
///     a separate DI path from <see cref="ServiceCollectionExtensions.AddEnrolmentEngine(IServiceCollection, IEnrolmentEngine)" />
///     that never registers an ambiguous container-wide <see cref="IEnrolmentEngine" />.
/// </summary>
public sealed class EnrolmentPolicyDependencyInjectionTests
{
	private static DirectoryDataSource StandardSource() => new(Harness.WorkflowsDir, Harness.DataDir);

	[Fact]
	public void add_enrolment_policies_registers_a_singleton_registry_built_eagerly()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentPolicies(options => {
			options.UseDefault("standard", "Standard", StandardSource()).UseFixedAsOf(Harness.AsOf);
		});

		using var provider = services.BuildServiceProvider();

		var first = provider.GetRequiredService<IEnrolmentPolicyRegistry>();
		var second = provider.GetRequiredService<IEnrolmentPolicyRegistry>();

		first.Should().BeSameAs(second);
		first.DefaultPolicyId.Should().Be(new("standard"));
		first.GetPolicy(new("standard")).Engine.Should().NotBeNull();
	}

	[Fact]
	public void two_configured_policies_are_both_resolvable_by_id()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentPolicies(options => {
			options.UseDefault("standard", "Standard", StandardSource())
				   .Add("elite", "Elite", StandardSource())
				   .UseFixedAsOf(Harness.AsOf);
		});

		using var provider = services.BuildServiceProvider();
		var registry = provider.GetRequiredService<IEnrolmentPolicyRegistry>();

		registry.Descriptors.Select(d => d.Id.Value).Should().Equal("standard", "elite");
		registry.GetPolicy(new("elite")).Descriptor.DisplayName.Should().Be("Elite");
	}

	[Fact]
	public void it_does_not_register_an_ambiguous_container_wide_engine()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentPolicies(options => {
			options.UseDefault("standard", "Standard", StandardSource()).UseFixedAsOf(Harness.AsOf);
		});

		using var provider = services.BuildServiceProvider();

		provider.GetService<IEnrolmentEngine>().Should().BeNull();
		provider.GetService<IEnrolmentEvaluator>().Should().BeNull();
	}

	[Fact]
	public void it_rejects_configuration_with_no_default_policy()
	{
		var services = new ServiceCollection();

		var act = () => services.AddEnrolmentPolicies(options => options.Add("standard", "Standard", StandardSource()));

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void it_rejects_configuration_with_no_policies_at_all()
	{
		var services = new ServiceCollection();

		var act = () => services.AddEnrolmentPolicies(static _ => { });

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void duplicate_policy_identifiers_surface_the_registry_configuration_exception()
	{
		var services = new ServiceCollection();

		var act = () => services.AddEnrolmentPolicies(options => {
			options.UseDefault("standard", "Standard", StandardSource()).Add("standard", "Standard Two", StandardSource());
		});

		act.Should().Throw<EnrolmentPolicyConfigurationException>();
	}

	[Fact]
	public void concurrent_reads_across_two_policies_do_not_interfere()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentPolicies(options => {
			options.UseDefault("standard", "Standard", StandardSource())
				   .Add("elite", "Elite", StandardSource())
				   .UseFixedAsOf(Harness.AsOf);
		});

		using var provider = services.BuildServiceProvider();
		var registry = provider.GetRequiredService<IEnrolmentPolicyRegistry>();
		var student = new StudentInput("S", new Dictionary<string, int> {
			["maths"] = 8,
		}, []);

		var results = Enumerable.Range(0, 40)
								.AsParallel()
								.Select(i => registry.GetPolicy(new(i % 2 == 0 ? "standard" : "elite")).Engine.Evaluate(student))
								.ToArray();

		results.Should().OnlyContain(static r => r != null);
	}
}
