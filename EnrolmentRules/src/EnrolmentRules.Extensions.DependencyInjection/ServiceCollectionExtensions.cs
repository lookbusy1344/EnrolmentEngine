namespace EnrolmentRules.Extensions.DependencyInjection;

using Engine;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Dependency-injection registration helpers for the enrolment engine.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	///     Register a pre-bootstrapped singleton engine. The instance is stateless and safe to reuse across
	///     requests. Use this when the host has already bootstrapped an engine (for example via
	///     <c>EnrolmentEngine.CreateAsync</c> or <see cref="AddEnrolmentEngineAsync" />).
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
	///     data directories. Await this before <c>BuildServiceProvider</c> so startup I/O stays async end-to-end.
	/// </summary>
	public static async Task<IServiceCollection> AddEnrolmentEngineAsync(
		this IServiceCollection services,
		Action<EnrolmentEngineOptions> configure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var options = new EnrolmentEngineOptions();
		configure(options);
		var engine = await options.CreateEngineAsync(cancellationToken).ConfigureAwait(false);
		return services.AddEnrolmentEngine(engine);
	}

	/// <summary>
	///     Bootstrap and register a reloadable <see cref="IEnrolmentEngineFactory" /> plus a singleton
	///     <see cref="IEnrolmentEngine" /> proxy that forwards each call to
	///     <see cref="IEnrolmentEngineFactory.Current" />.
	/// </summary>
	public static async Task<IServiceCollection> AddEnrolmentEngineFactoryAsync(
		this IServiceCollection services,
		Action<EnrolmentEngineOptions> configure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var options = new EnrolmentEngineOptions();
		configure(options);
		var factory = await options.CreateFactoryAsync(cancellationToken).ConfigureAwait(false);
		_ = services.AddSingleton<EnrolmentEngineFactory>(_ => factory);
		_ = services.AddSingleton<IEnrolmentEngineFactory>(static provider => provider.GetRequiredService<EnrolmentEngineFactory>());
		_ = services.AddSingleton<ReloadingEnrolmentEngineProxy>();
		_ = services.AddSingleton<IEnrolmentEvaluator>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());
		_ = services.AddSingleton<IEnrolmentAdvisor>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());
		_ = services.AddSingleton<IEnrolmentEngine>(static provider => provider.GetRequiredService<ReloadingEnrolmentEngineProxy>());

		return services;
	}
}
