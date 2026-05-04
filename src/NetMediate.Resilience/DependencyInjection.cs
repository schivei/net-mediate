using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.Resilience;

/// <summary>
/// Dependency injection extensions for NetMediate resilience behaviors.
/// </summary>
public static class DependencyInjection
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers retry, timeout, and circuit-breaker <em>options</em> for the resilience behaviors.
        /// Behaviors themselves are registered per message-type by the source generator using
        /// closed-type <c>configure.RegisterBehavior&lt;&gt;()</c> calls when
        /// <c>NetMediate.Resilience</c> is referenced — no open-generic registrations are used.
        /// </summary>
        /// <remarks>
        /// Call this method (optionally with configure delegates) before <c>AddNetMediateGenerated()</c>
        /// if you want non-default options. A second call from the generated code is a no-op because
        /// <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection,TService)"/>
        /// skips registration when the type is already present.
        /// </remarks>
        /// <param name="configureRetry">Optional retry options configuration.</param>
        /// <param name="configureTimeout">Optional timeout options configuration.</param>
        /// <param name="configureCircuitBreaker">Optional circuit-breaker options configuration.</param>
        /// <returns>The same service collection for chaining.</returns>
        public IServiceCollection AddNetMediateResilience(
            Action<RetryBehaviorOptions>? configureRetry = null,
            Action<TimeoutBehaviorOptions>? configureTimeout = null,
            Action<CircuitBreakerBehaviorOptions>? configureCircuitBreaker = null
        )
        {
            services.AddNetMediateRetry(configure: configureRetry);
            services.AddNetMediateTimeout(configure: configureTimeout);
            services.AddNetMediateCircuitBreaker(configure: configureCircuitBreaker);
            return services;
        }

        /// <summary>
        /// Registers the <see cref="RetryBehaviorOptions"/> singleton (default or custom).
        /// </summary>
        /// <param name="configure">Optional retry options configuration.</param>
        private void AddNetMediateRetry(Action<RetryBehaviorOptions>? configure = null)
        {
            var options = new RetryBehaviorOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
        }

        /// <summary>
        /// Registers the <see cref="TimeoutBehaviorOptions"/> singleton (default or custom).
        /// </summary>
        /// <param name="configure">Optional timeout options configuration.</param>
        private void AddNetMediateTimeout(Action<TimeoutBehaviorOptions>? configure = null)
        {
            var options = new TimeoutBehaviorOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
        }

        /// <summary>
        /// Registers the <see cref="CircuitBreakerBehaviorOptions"/> singleton (default or custom).
        /// </summary>
        /// <param name="configure">Optional circuit-breaker options configuration.</param>
        public void AddNetMediateCircuitBreaker(Action<CircuitBreakerBehaviorOptions>? configure = null)
        {
            var options = new CircuitBreakerBehaviorOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
        }
    }
}
