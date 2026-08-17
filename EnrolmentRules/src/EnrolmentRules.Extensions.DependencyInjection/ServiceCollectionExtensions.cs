namespace EnrolmentRules.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Dependency-injection registration helpers for the enrolment engine.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	///     Register a pre-bootstrapped singleton engine. The instance is stateless and safe to reuse across
	///     requests. Use this when the host has already bootstrapped an engine (for example via
	///     <c>EnrolmentEngine.Create</c> or <see cref="AddEnrolmentEngine(IServiceCollection,Action{EnrolmentEngineOptions},CancellationToken)" />).
	///     Registers every segregated capability interface — <see cref="IEnrolmentEngine" />,
	///     <see cref="IEnrolmentEvaluator" />, <see cref="IEnrolmentAdvisor" />, and
	///     <see cref="IEnrolmentCriteriaExplainer" /> — against the same singleton instance.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="services" /> or <paramref name="engine" /> is null.</exception>
	public static IServiceCollection AddEnrolmentEngine(this IServiceCollection services, IEnrolmentEngine engine)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(engine);

		_ = services.AddSingleton<IEnrolmentEngine>(engine);
		_ = services.AddSingleton<IEnrolmentEvaluator>(engine);
		_ = services.AddSingleton<IEnrolmentAdvisor>(engine);
		_ = services.AddSingleton<IEnrolmentCriteriaExplainer>(engine);
		if (engine is EnrolmentEngine concrete) {
			_ = services.AddSingleton(concrete);
		}

		return services;
	}

	/// <summary>
	///     Bootstrap and register a singleton <see cref="EnrolmentEngine" /> from the configured workflows and
	///     data directories.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="services" /> or <paramref name="configure" /> is null.</exception>
	public static IServiceCollection AddEnrolmentEngine(
		this IServiceCollection services,
		Action<EnrolmentEngineOptions> configure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var options = new EnrolmentEngineOptions();
		configure(options);
		var engine = options.CreateEngine(cancellationToken);
		return services.AddEnrolmentEngine(engine);
	}

	/// <summary>
	///     Bootstrap and register a reloadable <see cref="IEnrolmentEngineFactory" /> plus a singleton
	///     <see cref="IEnrolmentEngine" /> proxy that forwards each call to
	///     <see cref="IEnrolmentEngineFactory.Current" />. <see cref="IEnrolmentEvaluator" />,
	///     <see cref="IEnrolmentAdvisor" />, and <see cref="IEnrolmentCriteriaExplainer" /> resolve against
	///     the same proxy, so a reload is visible through every segregated interface without rebuilding the
	///     service provider.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="services" /> or <paramref name="configure" /> is null.</exception>
	public static IServiceCollection AddEnrolmentEngineFactory(
		this IServiceCollection services,
		Action<EnrolmentEngineOptions> configure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var options = new EnrolmentEngineOptions();
		configure(options);
		var factory = options.CreateFactory(cancellationToken);
		try {
			var ownedFactory = factory;
			_ = services.AddSingleton<EnrolmentEngineFactory>(_ => ownedFactory);
			_ = services.AddSingleton<IEnrolmentEngineFactory>(static provider => provider.GetRequiredService<EnrolmentEngineFactory>());
			_ = services.AddSingleton<ReloadingEnrolmentEngineProxy>();
			_ = services.AddSingleton<IEnrolmentEvaluator>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());
			_ = services.AddSingleton<IEnrolmentAdvisor>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());
			_ = services.AddSingleton<IEnrolmentCriteriaExplainer>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());
			_ = services.AddSingleton<IEnrolmentEngine>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());
			factory = null;
			return services;
		}
		finally {
			factory?.Dispose();
		}
	}

	/// <summary>
	///     Bootstrap and register a singleton multi-policy <see cref="EnrolmentPolicyRegistry" />. Every
	///     configured policy builds eagerly (a broken auxiliary policy fails startup). Consumers resolve
	///     <see cref="IEnrolmentPolicyRegistry" /> and select a policy explicitly per call — this path never
	///     also registers an ambiguous container-wide <see cref="IEnrolmentEngine" />, unlike
	///     <see cref="AddEnrolmentEngine(IServiceCollection, IEnrolmentEngine)" />.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="services" /> or <paramref name="configure" /> is null.</exception>
	public static IServiceCollection AddEnrolmentPolicies(
		this IServiceCollection services,
		Action<EnrolmentPolicyOptions> configure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var options = new EnrolmentPolicyOptions();
		configure(options);
		var registry = options.CreateRegistry(cancellationToken);

		_ = services.AddSingleton(registry);
		_ = services.AddSingleton<IEnrolmentPolicyRegistry>(registry);

		return services;
	}
}
