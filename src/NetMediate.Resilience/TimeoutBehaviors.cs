using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace NetMediate.Resilience;

internal static class TimeoutBehaviorRunner
{
    private static readonly object CompletedResult = new();

    public static ValueTask<TResponse> ExecuteAsync<TMessage, TResponse>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        TMessage message,
        Func<TMessage, CancellationToken, ValueTask<TResponse>> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull =>
        ExecuteCoreAsync(
            timeout,
            disabled,
            operationName,
            static async (state, ct) =>
                await state.Next(state.Message, ct).ConfigureAwait(false),
            (Message: message, Next: next),
            cancellationToken
        );

    public static async ValueTask ExecuteAsync<TMessage>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        TMessage message,
        Func<TMessage, CancellationToken, ValueTask> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        _ = await ExecuteCoreAsync(
                timeout,
                disabled,
                operationName,
                static async (state, ct) =>
                {
                    await state.Next(state.Message, ct).ConfigureAwait(false);
                    return CompletedResult;
                },
                (Message: message, Next: next),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public static async Task ExecuteAsync<TMessage>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        TMessage message,
        Func<TMessage, CancellationToken, Task> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        _ = await ExecuteCoreAsync(
                timeout,
                disabled,
                operationName,
                static async (state, ct) =>
                {
                    await state.Next(state.Message, ct).ConfigureAwait(false);
                    return CompletedResult;
                },
                (Message: message, Next: next),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public static IAsyncEnumerable<TResponse> ExecuteAsync<TMessage, TResponse>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        TMessage message,
        Func<TMessage, CancellationToken, IAsyncEnumerable<TResponse>> next,
        CancellationToken cancellationToken
        ) where TMessage : notnull =>
        ExecuteCoreAsync(
            timeout,
            disabled,
            operationName,
            static (state, ct) => state.Next(state.Message, ct),
            (Message: message, Next: next),
            cancellationToken
        );

    private static async ValueTask<TResult> ExecuteCoreAsync<TState, TResult>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        Func<TState, CancellationToken, ValueTask<TResult>> operation,
        TState state,
        CancellationToken cancellationToken
    )
    {
        if (disabled || timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return await operation(state, cancellationToken).ConfigureAwait(false);

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            return await operation(state, timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (timeoutTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{operationName} exceeded timeout '{timeout}'.", ex);
        }
    }

    private static async IAsyncEnumerable<TResult> ExecuteCoreAsync<TState, TResult>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        Func<TState, CancellationToken, IAsyncEnumerable<TResult>> operation,
        TState state,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (disabled || timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            await foreach (var item in operation(state, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        List<TResult> results = [];
        try
        {
            await foreach (var item in operation(state, timeoutTokenSource.Token).ConfigureAwait(false))
            {
                results.Add(item);
            }
        }
        catch (OperationCanceledException ex)
            when (timeoutTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{operationName} exceeded timeout '{timeout}'.", ex);
        }

        foreach (var item in results)
        {
            yield return item;
        }
    }
}

/// <summary>
/// Provides a base implementation of an asynchronous request handler that enforces a configurable timeout policy for
/// request processing.
/// </summary>
/// <remarks>This abstract class wraps an existing request handler and applies a timeout to its execution based on
/// the provided options. If the timeout is reached before the handler completes, the operation may be canceled or fail
/// according to the configured behavior. Use this class to ensure that request processing does not exceed a specified
/// duration.</remarks>
/// <typeparam name="TMessage">The type of the request message to handle. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
/// <param name="handler">The underlying request handler that processes the message.</param>
/// <param name="optionsAccessor">The options accessor that provides timeout configuration for request processing.</param>
public abstract class TimeoutRequestBehavior<TMessage, TResponse>(
    IRequestHandler<TMessage, TResponse> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : IRequestHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc/>
    public ValueTask<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
        TimeoutBehaviorRunner.ExecuteAsync(
            optionsAccessor.Value.RequestTimeout,
            optionsAccessor.Value.Disabled,
            "Request",
            message,
            handler.Handle,
            cancellationToken
        );
}

/// <summary>
/// Provides a base notification handler that applies a configurable timeout policy to the handling of notification
/// messages.
/// </summary>
/// <remarks>This abstract class enables derived notification handlers to enforce a timeout policy when processing
/// messages. The timeout duration and whether the timeout behavior is enabled are determined by the provided options.
/// Use this class to ensure that notification handling does not exceed a specified execution time.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must not be null.</typeparam>
/// <param name="handler">The underlying notification handler that processes the message.</param>
/// <param name="optionsAccessor">The options accessor that provides timeout configuration for the notification handler.</param>
public abstract class TimeoutNotificationBehavior<TMessage>(
    INotificationHandler<TMessage> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : INotificationHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc/>
    public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
        TimeoutBehaviorRunner.ExecuteAsync(
            optionsAccessor.Value.NotificationTimeout,
            optionsAccessor.Value.Disabled,
            "Notification",
            message,
            handler.Handle,
            cancellationToken
        );
}

/// <summary>
/// Provides a base command handler that enforces a configurable timeout for command execution.
/// </summary>
/// <remarks>This class wraps an existing command handler and applies a timeout policy to its execution. If the
/// timeout is disabled in the options, the command executes without a timeout. Use this class to ensure that command
/// handling does not exceed a specified duration.</remarks>
/// <typeparam name="TMessage">The type of the command message to be handled. Must not be null.</typeparam>
/// <param name="handler">The underlying command handler that processes the command message.</param>
/// <param name="optionsAccessor">The options accessor that supplies timeout configuration for command execution.</param>
public abstract class TimeoutCommandBehavior<TMessage>(
    ICommandHandler<TMessage> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : ICommandHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc/>
    public ValueTask Handle(TMessage message, CancellationToken cancellationToken = default) =>
        TimeoutBehaviorRunner.ExecuteAsync(
            optionsAccessor.Value.CommandTimeout,
            optionsAccessor.Value.Disabled,
            "Command",
            message,
            handler.Handle,
            cancellationToken
        );
}

public abstract class TimeoutStreamBehavior<TMessage, TResponse>(
    IStreamHandler<TMessage, TResponse> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : IStreamHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
        TimeoutBehaviorRunner.ExecuteAsync(
            optionsAccessor.Value.StreamTimeout,
            optionsAccessor.Value.Disabled,
            "Stream",
            message,
            handler.Handle,
            cancellationToken
        );
}
