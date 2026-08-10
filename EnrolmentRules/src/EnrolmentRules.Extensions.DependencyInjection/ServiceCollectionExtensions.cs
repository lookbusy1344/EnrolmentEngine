namespace EnrolmentRules.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Dependency-injection registration helpers for the enrolment engine.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	///     Register a pre-bootstrapped singleton engine. The instance is stateless and safe to reuse across
	///     requests. Use this when the host has already bootstrapped an engine (for example via
	///     <c>EnrolmentEngine.Create</c> or <see cref="AddEnrolmentEngine(IServiceCollection,Action{EnrolmentEngineOptions},CancellationToken)" />).
	/// </summary>
	public static IServiceCollection AddEnrolmentEngine(this IServiceCollection services, IEnrolmentEngine engine)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(engine);

		_ = services.AddSingleton<IEnrolmentEngine>(engine);
		_ = services.AddSingleton<IEnrolmentEvaluator>(engine);
		_ = services.AddSingleton<IEnrolmentAdvisor>(engine);
		if (engine is EnrolmentEngine concrete) {
			_ = services.AddSingleton(concrete);
		}

		return services;
	}

	/// <summary>
	///     Bootstrap and register a singleton <see cref="EnrolmentEngine" /> from the configured workflows and
	///     data directories.
	/// </summary>
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
	///     <see cref="IEnrolmentEngineFactory.Current" />.
	/// </summary>
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
			_ = services.AddSingleton<IEnrolmentEngine>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());
			factory = null;
			return services;
		}
		finally {
			factory?.Dispose();
		}
	}
}
