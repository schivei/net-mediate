using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Resilience;

public static class DependencyInjection
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers retry, timeout and circuit-breaker behaviors for request and notification pipelines.
        /// </summary>
        /// <param name="configureRetry">Optional retry options configuration.</param>
        /// <param name="configureTimeout">Optional timeout options configuration.</param>
        /// <param name="configureCircuitBreaker">Optional circuit-breaker options configuration.</param>
        /// <returns>The same service collection for chaining.</returns>
        public IServiceCollection AddNetMediateResilience(Action<RetryBehaviorOptions>? configureRetry = null,
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
        /// Registers retry behaviors for request and notification pipelines.
        /// </summary>
        /// <param name="configure">Optional retry options configuration.</param>
        /// <returns>The same service collection for chaining.</returns>
        public void AddNetMediateRetry(Action<RetryBehaviorOptions>? configure = null)
        {
            var options = new RetryBehaviorOptions();
            configure?.Invoke(options);
            services
                .AddSingleton(options)
                .AddSingleton(typeof(IPipelineBehavior<,>), typeof(RetryRequestBehavior<,>))
                .AddSingleton(typeof(IPipelineBehavior<,>), typeof(RetryNotificationBehavior<>));
        }

        /// <summary>
        /// Registers timeout behaviors for request and notification pipelines.
        /// </summary>
        /// <param name="configure">Optional timeout options configuration.</param>
        /// <returns>The same service collection for chaining.</returns>
        public void AddNetMediateTimeout(Action<TimeoutBehaviorOptions>? configure = null)
        {
            var options = new TimeoutBehaviorOptions();
            configure?.Invoke(options);
            services.AddSingleton(options)
                .AddSingleton(typeof(IPipelineBehavior<,>), typeof(TimeoutRequestBehavior<,>))
                .AddSingleton(typeof(IPipelineBehavior<,>), typeof(TimeoutNotificationBehavior<>));
        }

        /// <summary>
        /// Registers circuit-breaker behaviors for request and notification pipelines.
        /// </summary>
        /// <param name="configure">Optional circuit-breaker options configuration.</param>
        /// <returns>The same service collection for chaining.</returns>
        public void AddNetMediateCircuitBreaker(Action<CircuitBreakerBehaviorOptions>? configure = null)
        {
            var options = new CircuitBreakerBehaviorOptions();
            configure?.Invoke(options);
            services.AddSingleton(options)
                .AddSingleton(typeof(IPipelineBehavior<,>), typeof(CircuitBreakerRequestBehavior<,>))
                .AddSingleton(typeof(IPipelineBehavior<,>), typeof(CircuitBreakerNotificationBehavior<>));
        }
    }
}
