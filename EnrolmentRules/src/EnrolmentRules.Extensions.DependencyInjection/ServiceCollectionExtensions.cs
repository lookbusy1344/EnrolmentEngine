namespace EnrolmentRules.Extensions.DependencyInjection;

using Engine;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Dependency-injection registration helpers for the enrolment engine.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	///     Register a singleton <see cref="EnrolmentEngine" /> after bootstrapping the shipped workflows,
	///     catalogue and thresholds. The engine itself is stateless and safe to reuse across requests.
	/// </summary>
	public static IServiceCollection AddEnrolmentEngine(
		this IServiceCollection services,
		Action<EnrolmentEngineOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var options = new EnrolmentEngineOptions();
		configure(options);
		options.Validate();

		// Service registration is synchronous; this one-time bootstrap intentionally blocks at container build.
		// CreateAsync's awaits use ConfigureAwait(false), so this blocking resolve does not deadlock a host that
		// carries a SynchronizationContext.
#pragma warning disable VSTHRD002
		_ = services.AddSingleton(_ =>
			EnrolmentEngine.CreateAsync(
				options.WorkflowsDirectory,
				options.DataDirectory,
				options.AsOfSource()).GetAwaiter().GetResult());
#pragma warning restore VSTHRD002

		// Expose the same singleton through the abstraction so consumers can depend on IEnrolmentEngine and
		// substitute a fake in their own tests.
		_ = services.AddSingleton<IEnrolmentEngine>(static sp => sp.GetRequiredService<EnrolmentEngine>());

		return services;
	}
}
