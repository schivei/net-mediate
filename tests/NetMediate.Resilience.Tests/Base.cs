using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace NetMediate.Resilience.Tests;


public sealed record RetryRequestMessage(string Method);
public sealed record RetryDisabledMessage(string Method);
public sealed record RetryCommandMessage(string Method);
public sealed record RetryNotificationMessage(string Method);
public sealed record TimeoutRequestMessage(string Method);
public sealed record TimeoutCommandMessage(string Method);
public sealed record TimeoutNotificationMessage(string Method);
public sealed record CircuitRequestMessage(string Method);
public sealed record CircuitCommandMessage(string Method);
public sealed record CircuitNotificationMessage(string Method);
public sealed record CircuitStreamMessage(string Method);
public sealed record RetryStreamMessage(string Method);
public sealed record RetryStreamDisabledMessage(string Method);
public sealed record TimeoutStreamMessage(string Method);
public sealed record TimeoutStreamDisabledMessage(string Method);
public sealed record Response(int Value);

[Injectable(ServiceLifetime.Singleton)]
internal sealed class Attemption
{
    private readonly ConcurrentDictionary<string, int> _attempts = [];

    public int Attempt(string method)
    {
        return _attempts.AddOrUpdate(
            method,
            addValueFactory: _ => 1,
            updateValueFactory: (_, current) => current + 1
        );
    }

    public int Get(string method) => _attempts.TryGetValue(method, out var count) ? count : 0;
}

[Injectable]
internal sealed class RequestRetryRequestTestRetryRequest(Attemption attemption) :
    LambdaRequestHandler<RetryRequestMessage, Response>((msg, _) =>
    {
        var attempts = attemption.Attempt(msg.Method);
        return attempts < 3
            ? ValueTask.FromException<Response>(new InvalidOperationException("fail"))
            : ValueTask.FromResult(new Response(42));
    });

[Injectable]
internal sealed class RequestRetryDisabledTestRetryRequest(Attemption attemption) :
    LambdaRequestHandler<RetryDisabledMessage, Response>((msg, _) =>
    {
        attemption.Attempt(msg.Method);
        return ValueTask.FromException<Response>(new InvalidOperationException("fail"));
    });

[Injectable]
internal sealed class SendRetryCommandTestRetryCommand(Attemption attemption) :
    LambdaCommandHandler<RetryCommandMessage>((msg, _) =>
    {
        var attempts = attemption.Attempt(msg.Method);
        return attempts == 1
            ? ValueTask.FromCanceled(new CancellationToken(canceled: true))
            : ValueTask.CompletedTask;
    });

[Injectable]
internal sealed class NotifyRetryNotificationTestRetryNotification(Attemption attemption, CountdownEvent semaphore) :
    LambdaNotificationHandler<RetryNotificationMessage>((msg, _) =>
    {
        try
        {
            var attempts = attemption.Attempt(msg.Method);
            return attempts < 3
                ? ValueTask.FromException(new InvalidOperationException("fail"))
                : ValueTask.CompletedTask;
        }
        finally
        {
            semaphore.Signal();
        }
    });

[Injectable]
internal sealed class RequestTimeoutRequestTestTimeoutRequest(Attemption attemption) :
    LambdaRequestHandler<TimeoutRequestMessage, Response>(async (msg, cancellationToken) =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        attemption.Attempt(msg.Method);
        return new Response(1);
    });

internal class LambdaRequestHandler<TMessage, TResponse>(
    Func<TMessage, CancellationToken, ValueTask<TResponse>> callback
) : IRequestHandler<TMessage, TResponse>
    where TMessage : notnull
{
    public ValueTask<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
        callback(message, cancellationToken);
}

internal class LambdaCommandHandler<TMessage>(
    Func<TMessage, CancellationToken, ValueTask> callback
) : ICommandHandler<TMessage>
    where TMessage : notnull
{
    public ValueTask Handle(TMessage message, CancellationToken cancellationToken = default) =>
        callback(message, cancellationToken);
}

internal class LambdaNotificationHandler<TMessage>(
    Func<TMessage, CancellationToken, ValueTask> callback
) : INotificationHandler<TMessage>
    where TMessage : notnull
{
    public ValueTask Handle(TMessage message, CancellationToken cancellationToken = default) =>
        callback(message, cancellationToken);
}

[DecoratorFor]
internal sealed class CircuitStreamCircuitBreakerStreamBehavior(
    IStreamHandler<CircuitStreamMessage, Response> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : CircuitBreakerStreamBehavior<CircuitStreamMessage, Response>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class CircuitNotificationCircuitBreakerNotificationBehavior(
    INotificationHandler<CircuitNotificationMessage> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : CircuitBreakerNotificationBehavior<CircuitNotificationMessage>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class CircuitCommandCircuitBreakerCommandBehavior(
    ICommandHandler<CircuitCommandMessage> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : CircuitBreakerCommandBehavior<CircuitCommandMessage>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class CircuitRequestCircuitBreakerRequestBehavior(
    IRequestHandler<CircuitRequestMessage, Response> handler,
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : CircuitBreakerRequestBehavior<CircuitRequestMessage, Response>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class RetryRequestTestRetryStreamBehavior(
    IStreamHandler<RetryStreamMessage, Response> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryStreamBehavior<RetryStreamMessage, Response>(handler, optionsAccessor);

[Injectable]
internal sealed class RetryStreamLambdaStreamHandler(
    Func<RetryStreamMessage, CancellationToken, IAsyncEnumerable<Response>> callback
) : LambdaStreamHandler<RetryStreamMessage, Response>(callback);

[Injectable]
internal sealed class RetryStreamDisabledLambdaStreamHandler(
    Func<RetryStreamDisabledMessage, CancellationToken, IAsyncEnumerable<Response>> callback
) : LambdaStreamHandler<RetryStreamDisabledMessage, Response>(callback);

[Injectable]
internal sealed class TimeoutStreamLambdaStreamHandler(
    Func<TimeoutStreamMessage, CancellationToken, IAsyncEnumerable<Response>> callback
) : LambdaStreamHandler<TimeoutStreamMessage, Response>(callback);

[Injectable]
internal sealed class TimeoutStreamDisabledLambdaStreamHandler(
    Func<TimeoutStreamDisabledMessage, CancellationToken, IAsyncEnumerable<Response>> callback
) : LambdaStreamHandler<TimeoutStreamDisabledMessage, Response>(callback);

[Injectable]
internal sealed class CircuitStreamLambdaStreamHandler(
    Func<CircuitStreamMessage, CancellationToken, IAsyncEnumerable<Response>> callback
) : LambdaStreamHandler<CircuitStreamMessage, Response>(callback);

internal abstract class LambdaStreamHandler<TMessage, TResponse>(
    Func<TMessage, CancellationToken, IAsyncEnumerable<TResponse>> callback
) : IStreamHandler<TMessage, TResponse>
    where TMessage : notnull
{
    public IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        CancellationToken cancellationToken = default
    ) => callback(message, cancellationToken);
}

[DecoratorFor]
internal sealed class RetryRequestTestRetryRequestBehavior(
    IRequestHandler<RetryRequestMessage, Response> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryRequestBehavior<RetryRequestMessage, Response>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class RetryRequestDisabledTestRetryRequestBehavior(
    IRequestHandler<RetryDisabledMessage, Response> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryRequestBehavior<RetryDisabledMessage, Response>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class RetryCommandTestRetryCommandBehavior(
    ICommandHandler<RetryCommandMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : TestRetryCommandBehavior<RetryCommandMessage>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class RetryCommandDisabledTestRetryCommandBehavior(
    ICommandHandler<RetryDisabledMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : TestRetryCommandBehavior<RetryDisabledMessage>(handler, optionsAccessor);

internal abstract class TestRetryCommandBehavior<TMessage>(
    ICommandHandler<TMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryCommandBehavior<TMessage>(handler, optionsAccessor)
    where TMessage : notnull;

[DecoratorFor]
internal sealed class RetryNotificationTestRetryNotificationBehavior(
    INotificationHandler<RetryNotificationMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : TestRetryNotificationBehavior<RetryNotificationMessage>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class RetryNotificationDisabledTestRetryNotificationBehavior(
    INotificationHandler<RetryDisabledMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : TestRetryNotificationBehavior<RetryDisabledMessage>(handler, optionsAccessor);

internal abstract class TestRetryNotificationBehavior<TMessage>(
    INotificationHandler<TMessage> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryNotificationBehavior<TMessage>(handler, optionsAccessor)
    where TMessage : notnull;

internal abstract class TestTimeoutRequestBehavior<TMessage, TResponse>(
    IRequestHandler<TMessage, TResponse> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TimeoutRequestBehavior<TMessage, TResponse>(handler, optionsAccessor)
    where TMessage : notnull;

[DecoratorFor]
internal sealed class TimeoutCommandTestTimeoutCommandBehavior(
    ICommandHandler<TimeoutCommandMessage> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TestTimeoutCommandBehavior<TimeoutCommandMessage>(handler, optionsAccessor);

internal abstract class TestTimeoutCommandBehavior<TMessage>(
    ICommandHandler<TMessage> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TimeoutCommandBehavior<TMessage>(handler, optionsAccessor)
    where TMessage : notnull;

[DecoratorFor]
internal sealed class TimeoutNotificationTestTimeoutNotificationBehavior(
    INotificationHandler<TimeoutNotificationMessage> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TestTimeoutNotificationBehavior<TimeoutNotificationMessage>(handler, optionsAccessor);

internal abstract class TestTimeoutNotificationBehavior<TMessage>(
    INotificationHandler<TMessage> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TimeoutNotificationBehavior<TMessage>(handler, optionsAccessor)
    where TMessage : notnull;

[DecoratorFor]
internal sealed class RetryStreamTestRetryStreamBehavior(
    IStreamHandler<RetryStreamMessage, Response> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : TestRetryStreamBehavior<RetryStreamMessage, Response>(handler, optionsAccessor);

[DecoratorFor]
internal sealed class RetryStreamDisabledTestRetryStreamBehavior(
    IStreamHandler<RetryStreamDisabledMessage, Response> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : TestRetryStreamBehavior<RetryStreamDisabledMessage, Response>(handler, optionsAccessor);

internal abstract class TestRetryStreamBehavior<TMessage, TResponse>(
    IStreamHandler<TMessage, TResponse> handler,
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryStreamBehavior<TMessage, TResponse>(handler, optionsAccessor)
    where TMessage : notnull;

[DecoratorFor]
internal sealed class TimeoutStreamTestTimeoutStreamBehavior(
    IStreamHandler<TimeoutStreamMessage, Response> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TestTimeoutStreamBehavior<TimeoutStreamMessage, Response>(handler, optionsAccessor);


[DecoratorFor]
internal sealed class TimeoutRequestTestTimeoutRequestBehavior(
    IRequestHandler<TimeoutRequestMessage, Response> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TestTimeoutRequestBehavior<TimeoutRequestMessage, Response>(handler, optionsAccessor);


[DecoratorFor]
internal sealed class TimeoutStreamDisabledTestTimeoutStreamBehavior(
    IStreamHandler<TimeoutStreamDisabledMessage, Response> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TestTimeoutStreamBehavior<TimeoutStreamDisabledMessage, Response>(handler, optionsAccessor);

internal abstract class TestTimeoutStreamBehavior<TMessage, TResponse>(
    IStreamHandler<TMessage, TResponse> handler,
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TimeoutStreamBehavior<TMessage, TResponse>(handler, optionsAccessor)
    where TMessage : notnull;
