using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace NetMediate.Resilience;

/// <summary>
/// Provides a MediatR request handler decorator that applies a circuit breaker pattern to requests, preventing
/// execution when the circuit is open due to repeated failures.
/// </summary>
/// <remarks>This behavior can be used to automatically short-circuit requests when repeated failures are
/// detected, improving system resilience. When the circuit is open, requests are not forwarded to the underlying
/// handler and an exception is thrown instead.</remarks>
/// <typeparam name="TMessage">The type of the request message. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
/// <param name="handler">The underlying request handler to be decorated with circuit breaker behavior.</param>
/// <param name="optionsAccessor">The options accessor used to configure circuit breaker behavior.</param>
public sealed class CircuitBreakerRequestBehavior<TMessage, TResponse>(
    IRequestHandler<TMessage, TResponse> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : ACircuitBreakerBehavior<TMessage, Task<TResponse>>("Circuit open for request.", optionsAccessor), IRequestHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public override Task<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
        ExecuteRequestAsync(message, handler.Handle, cancellationToken);
}

/// <summary>
/// Provides a notification handler decorator that applies circuit breaker logic to notification handling operations.
/// </summary>
/// <remarks>This behavior prevents notification handling when the circuit is open, helping to protect downstream
/// systems from repeated failures. It is intended for use with MediatR notification handlers.</remarks>
/// <typeparam name="TMessage">The type of the notification message to handle. Must not be null.</typeparam>
/// <param name="handler">The underlying notification handler to which the circuit breaker behavior is applied.</param>
/// <param name="optionsAccessor">The options accessor used to configure circuit breaker behavior.</param>
public sealed class CircuitBreakerNotificationBehavior<TMessage>(
    INotificationHandler<TMessage> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : ACircuitBreakerBehavior<TMessage, Task>("Circuit open for notification.", optionsAccessor), INotificationHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public override Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
        ExecuteAsync(message, handler.Handle, cancellationToken);
}

/// <summary>
/// Provides circuit breaker behavior for command handlers, preventing command execution when the circuit is open due to
/// repeated failures.
/// </summary>
/// <remarks>This behavior monitors command execution and temporarily blocks further executions if failures exceed
/// configured thresholds, helping to prevent cascading failures in the system. The circuit breaker state and thresholds
/// are controlled by the provided options.</remarks>
/// <typeparam name="TMessage">The type of the command message to handle. Must not be null.</typeparam>
/// <param name="handler">The command handler to wrap with circuit breaker behavior.</param>
/// <param name="optionsAccessor">The options accessor that provides configuration for the circuit breaker behavior.</param>
public sealed class CircuitBreakerCommandBehavior<TMessage>(
    ICommandHandler<TMessage> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : ACircuitBreakerBehavior<TMessage, Task>("Circuit open for command.", optionsAccessor), ICommandHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public override Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
        ExecuteAsync(message, handler.Handle, cancellationToken);
}

/// <summary>
/// Provides a stream handler decorator that applies circuit breaker logic to asynchronous streaming operations,
/// preventing calls to the underlying handler when the circuit is open.
/// </summary>
/// <remarks>This behavior can be used to protect downstream streaming handlers from repeated failures by
/// short-circuiting calls when the circuit is open. It is typically used in scenarios where resilience and fault
/// tolerance are required for streaming operations.</remarks>
/// <typeparam name="TMessage">The type of the message handled by the stream handler. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response elements produced by the stream handler.</typeparam>
/// <param name="handler">The underlying stream handler to which requests are delegated when the circuit is closed.</param>
/// <param name="optionsAccessor">The options accessor that provides configuration settings for the circuit breaker behavior.</param>
public sealed class CircuitBreakerStreamBehavior<TMessage, TResponse>(
    IStreamHandler<TMessage, TResponse> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : ACircuitBreakerBehavior<TMessage, IAsyncEnumerable<TResponse>>("Circuit open for stream.", optionsAccessor), IStreamHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public override IAsyncEnumerable<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
        ExecuteStreamAsync(message, handler.Handle, cancellationToken);
}

/// <summary>
/// Provides a base class for implementing circuit breaker behaviors for message handlers, enabling automatic
/// short-circuiting of operations after repeated failures.
/// </summary>
/// <remarks>This abstract class is intended to be used as a base for implementing circuit breaker patterns in
/// message handling pipelines. It tracks consecutive failures and temporarily prevents further executions when a
/// configurable failure threshold is reached, resuming normal operation after a specified duration. The circuit breaker
/// can be disabled via configuration. Thread safety is ensured for all state transitions.</remarks>
/// <typeparam name="TMessage">The type of the message handled by the circuit breaker. Must not be null.</typeparam>
/// <typeparam name="TResult">The type of the result returned by the handler.</typeparam>
/// <param name="circuitOpenMessage">The message to include in exceptions thrown when the circuit is open.</param>
/// <param name="optionsAccessor">The options accessor that provides configuration settings for the circuit breaker behavior.</param>
public abstract class ACircuitBreakerBehavior<TMessage, TResult>(
    string circuitOpenMessage,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : IHandler<TMessage, TResult>
    where TMessage : notnull
{
    private static readonly Lock s_sync = new();
    private static int s_consecutiveFailures;
    private static DateTimeOffset? s_openUntil;

    /// <summary>
    /// Determines whether the current feature or service is disabled based on configuration options.
    /// </summary>
    /// <returns>true if the feature or service is disabled; otherwise, false.</returns>
    protected bool IsDisabled() => optionsAccessor.Value.Disabled;

    /// <summary>
    /// Executes the specified asynchronous request delegate, applying circuit breaker logic to control execution based
    /// on the current circuit state.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response returned by the request delegate.</typeparam>
    /// <param name="message">The message or request object to be processed by the delegate.</param>
    /// <param name="next">A delegate representing the next operation in the pipeline to execute. This function receives the message and
    /// cancellation token, and returns a task that produces the response.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the response produced by the request
    /// delegate.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the circuit is open and requests are not allowed to proceed.</exception>
    protected async Task<TResponse> ExecuteRequestAsync<TResponse>(
        TMessage message,
        Func<TMessage, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
    {
        if (IsDisabled())
        {
            var result = await next(message, cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
            return result;
        }

        if (IsCircuitOpen())
            throw new InvalidOperationException(circuitOpenMessage);

        try
        {
            var result = await next(message, cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
            return result;
        }
        catch
        {
            RegisterFailure();
            throw;
        }
    }

    /// <summary>
    /// Executes the provided asynchronous streaming delegate within the circuit breaker, yielding each response as it
    /// is produced.
    /// </summary>
    /// <remarks>If the circuit breaker is disabled, the delegate is executed without circuit breaker logic.
    /// On failure, the circuit breaker is notified to track the failure state.</remarks>
    /// <typeparam name="TResponse">The type of the streamed response elements.</typeparam>
    /// <param name="message">The message to be passed to the streaming delegate.</param>
    /// <param name="next">A delegate that initiates the asynchronous streaming operation using the specified message and cancellation
    /// token.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the streaming operation.</param>
    /// <returns>An asynchronous stream of response elements produced by the delegate.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the circuit breaker is open and streaming is not permitted.</exception>
    protected async IAsyncEnumerable<TResponse> ExecuteStreamAsync<TResponse>(
        TMessage message,
        Func<TMessage, CancellationToken, IAsyncEnumerable<TResponse>> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (IsDisabled())
        {
            await foreach (var item in next(message, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
            RegisterSuccess();
            yield break;
        }

        if (IsCircuitOpen())
            throw new InvalidOperationException(circuitOpenMessage);

        bool succeeded = false;
        try
        {
            await foreach (var item in next(message, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
            succeeded = true;
            RegisterSuccess();
        }
        finally
        {
            if (!succeeded)
            {
                RegisterFailure();
            }
        }
    }

    /// <summary>
    /// Executes the asynchronous pipeline step for the specified message, invoking the next delegate in the pipeline
    /// unless the circuit is open or the step is disabled.
    /// </summary>
    /// <param name="message">The message to process in the pipeline step.</param>
    /// <param name="next">A delegate representing the next step in the pipeline to be invoked with the message and cancellation token.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous execution of the pipeline step.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the circuit is open and the pipeline step cannot be executed.</exception>
    protected async Task ExecuteAsync(
        TMessage message,
        Func<TMessage, CancellationToken, Task> next,
        CancellationToken cancellationToken
    )
    {
        if (IsDisabled())
        {
            await next(message, cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
            return;
        }

        if (IsCircuitOpen())
            throw new InvalidOperationException(circuitOpenMessage);

        try
        {
            await next(message, cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
        }
        catch
        {
            RegisterFailure();
            throw;
        }
    }

    /// <summary>
    /// Determines whether the circuit breaker is currently open, preventing operations from proceeding.
    /// </summary>
    /// <remarks>This method is typically used in circuit breaker implementations to check if the system
    /// should temporarily reject requests due to recent failures. The circuit remains open until a specified timeout
    /// elapses.</remarks>
    /// <returns>true if the circuit is open and operations should be blocked; otherwise, false.</returns>
    protected static bool IsCircuitOpen()
    {
        lock (s_sync)
        {
            if (s_openUntil is null)
                return false;

            if (DateTimeOffset.UtcNow < s_openUntil.Value)
                return true;

            s_openUntil = null;
            s_consecutiveFailures = 0;
            return false;
        }
    }

    /// <summary>
    /// Resets the internal failure tracking state to indicate a successful operation.
    /// </summary>
    /// <remarks>This method should be called after a successful operation to clear any recorded consecutive
    /// failures and reset related state. It is intended for use in scenarios where failure tracking is used to control
    /// access or circuit breaker logic.</remarks>
    protected static void RegisterSuccess()
    {
        lock (s_sync)
        {
            s_consecutiveFailures = 0;
            s_openUntil = null;
        }
    }

    /// <summary>
    /// Registers a failure occurrence and updates the failure tracking state using the configured threshold and open
    /// duration values.
    /// </summary>
    /// <remarks>This method retrieves the failure threshold and open duration from the current options and
    /// delegates to the overload that accepts these parameters. It is typically used to record a failure event in
    /// scenarios such as circuit breaker implementations.</remarks>
    protected void RegisterFailure()
    {
        var options = optionsAccessor.Value;
        var threshold = Math.Max(1, options.FailureThreshold);
        var openDuration =
            options.OpenDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : options.OpenDuration;

        RegisterFailure(threshold, openDuration);
    }

    private static void RegisterFailure(int threshold, TimeSpan openDuration)
    {
        lock (s_sync)
        {
            s_consecutiveFailures++;
            if (s_consecutiveFailures < threshold)
                return;

            s_openUntil = DateTimeOffset.UtcNow.Add(openDuration);
            s_consecutiveFailures = 0;
        }
    }

    /// <inheritdoc />
    public abstract TResult Handle(TMessage message, CancellationToken cancellationToken = default);
}
