using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace NetMediate.Resilience;

internal static class RetryBehaviorRunner
{
    private static readonly object CompletedResult = new();

    public static Task<TResponse> ExecuteAsync<TMessage, TResponse>(
        IOptions<RetryBehaviorOptions> optionsAccessor,
        TMessage message,
        Func<TMessage, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull =>
        ExecuteCoreAsync(
            optionsAccessor.Value,
            static async (state, ct) =>
                await state.Next(state.Message, ct).ConfigureAwait(false),
            (Message: message, Next: next),
            cancellationToken
        );

    public static IAsyncEnumerable<TResponse> ExecuteAsync<TMessage, TResponse>(
        IOptions<RetryBehaviorOptions> optionsAccessor,
        TMessage message,
        Func<TMessage, CancellationToken, IAsyncEnumerable<TResponse>> next,
        CancellationToken cancellationToken
        ) where TMessage : notnull =>
        ExecuteCoreAsync(
            optionsAccessor.Value,
            static (state, ct) => state.Next(state.Message, ct),
            (Message: message, Next: next),
            cancellationToken
        );

    public static async Task ExecuteAsync<TMessage>(
        IOptions<RetryBehaviorOptions> optionsAccessor,
        TMessage message,
        Func<TMessage, CancellationToken, Task> next,
        CancellationToken cancellationToken
    ) where TMessage : notnull
    {
        _ = await ExecuteCoreAsync(
                optionsAccessor.Value,
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

    private static async Task<TResult> ExecuteCoreAsync<TMessage, TResult>(
        RetryBehaviorOptions options,
        Func<TMessage, CancellationToken, Task<TResult>> operation,
        TMessage message,
        CancellationToken cancellationToken
    )
    {
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        if (options.Disabled)
            return await operation(message, cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && attempt < maxRetryCount
            )
            {
                await DelayIfNeededAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < maxRetryCount)
            {
                await DelayIfNeededAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }

    }

    private static async IAsyncEnumerable<TResult> ExecuteCoreAsync<TMessage, TResult>(
        RetryBehaviorOptions options,
        Func<TMessage, CancellationToken, IAsyncEnumerable<TResult>> operation,
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        if (options.Disabled)
        {
            await foreach (var item in operation(message, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        List<TResult> results = [];

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Clear();

            try
            {
                await foreach (var item in operation(message, cancellationToken).ConfigureAwait(false))
                {
                    results.Add(item);
                }

                break;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && attempt < maxRetryCount
            )
            {
                await DelayIfNeededAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < maxRetryCount)
            {
                await DelayIfNeededAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var item in results)
        {
            yield return item;
        }
    }

    private static Task DelayIfNeededAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero
            ? Task.Delay(delay, cancellationToken)
            : Task.CompletedTask;
}

/// <summary>
/// Provides a base implementation for stream handlers that adds retry behavior to the handling of streaming messages.
/// </summary>
/// <remarks>This abstract class decorates an existing stream handler to automatically apply retry logic when
/// handling streaming messages. The retry behavior is configured via the provided options. Derived classes can extend
/// or customize the retry strategy as needed.</remarks>
/// <typeparam name="TMessage">The type of the message received by the stream handler. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response produced by the stream handler.</typeparam>
/// <param name="handler">The underlying stream handler that processes messages and produces responses.</param>
/// <param name="optionsAccessor">The options accessor that provides configuration for the retry behavior.</param>
public abstract class RetryStreamBehavior<TMessage, TResponse>(
    IStreamHandler<TMessage, TResponse> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : IStreamHandler<TMessage, TResponse> where TMessage : notnull
{
    public IAsyncEnumerable<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
        RetryBehaviorRunner.ExecuteAsync(optionsAccessor, message, handler.Handle, cancellationToken);
}

/// <summary>
/// Provides a base implementation of a request handler that applies retry logic to the handling of requests.
/// </summary>
/// <remarks>This class enables automatic retry of failed requests according to the specified retry options. It
/// can be used as a base for implementing resilient request handling in scenarios where transient failures are
/// expected.</remarks>
/// <typeparam name="TMessage">The type of the request message to be handled. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
/// <param name="handler">The underlying request handler that processes the message and produces a response.</param>
/// <param name="optionsAccessor">The options accessor that supplies configuration settings for the retry behavior.</param>
public abstract class RetryRequestBehavior<TMessage, TResponse>(
    IRequestHandler<TMessage, TResponse> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : IRequestHandler<TMessage, TResponse> where TMessage : notnull
{
    /// <inheritdoc/>
    public Task<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
        RetryBehaviorRunner.ExecuteAsync(optionsAccessor, message, handler.Handle, cancellationToken);
}

/// <summary>
/// Provides a base notification handler that applies retry logic to the handling of notification messages.
/// </summary>
/// <remarks>This abstract class enables retry policies for notification handlers by wrapping the execution of the
/// handler with retry logic. Use this as a base class to add retry capabilities to implementations of
/// INotificationHandler<TMessage>.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must not be null.</typeparam>
/// <param name="handler">The underlying notification handler that processes the message.</param>
/// <param name="optionsAccessor">The options accessor that provides configuration for retry behavior.</param>
public abstract class RetryNotificationBehavior<TMessage>(
    INotificationHandler<TMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : INotificationHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc/>
    public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
        RetryBehaviorRunner.ExecuteAsync(optionsAccessor, message, handler.Handle, cancellationToken);
}

/// <summary>
/// Provides a command handler decorator that adds retry logic to command execution based on configurable options.
/// </summary>
/// <remarks>This class enables automatic retry of command handling operations according to the specified retry
/// policy. It can be used to improve resilience when handling transient failures in command processing.</remarks>
/// <typeparam name="TMessage">The type of the command message to be handled. Must not be null.</typeparam>
/// <param name="handler">The underlying command handler to which retry behavior will be applied.</param>
/// <param name="optionsAccessor">The options accessor that supplies configuration settings for retry behavior.</param>
public abstract class RetryCommandBehavior<TMessage>(
    ICommandHandler<TMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : ICommandHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc/>
    public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
        RetryBehaviorRunner.ExecuteAsync(optionsAccessor, message, handler.Handle, cancellationToken);
}
