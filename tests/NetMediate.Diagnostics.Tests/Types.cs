using System.Runtime.CompilerServices;

namespace NetMediate.Diagnostics.Tests;

public sealed record CommandMessage
{
    public bool Called { get; set; }
    public Exception? Exception { get; init; }
}
public sealed record NotificationMessage
{
    public bool Called { get; set; }
    public Exception? Exception { get; init; }
}
public sealed record RequestMessage
{
    public bool Called { get; set; }
    public Exception? Exception { get; init; }
}
public sealed record Response(string Value);
public sealed record StreamMessage
{
    public bool Called { get; set; }
    public Exception? Exception { get; init; }
}
public sealed record StreamResponse(string Value);

[Injectable]
internal sealed class CommandHandler : ICommandHandler<CommandMessage>
{
    public ValueTask Handle(CommandMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Exception is not null)
            return ValueTask.FromException(message.Exception);

        message.Called = true;
        return ValueTask.CompletedTask;
    }
}

[Injectable]
internal sealed class NotificationHandler(CountdownEvent countdownEvent) : INotificationHandler<NotificationMessage>
{
    public ValueTask Handle(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            if (message.Exception is not null)
                return ValueTask.FromException(message.Exception);

            message.Called = true;
            return ValueTask.CompletedTask;
        }
        finally
        {
            countdownEvent.Signal();
        }
    }
}

[Injectable]
internal sealed class RequestHandler : IRequestHandler<RequestMessage, Response>
{
    public ValueTask<Response> Handle(RequestMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Exception is not null)
            return ValueTask.FromException<Response>(message.Exception);

        message.Called = true;
        return ValueTask.FromResult(new Response("ok"));
    }
}

[Injectable]
internal sealed class StreamHandler : IStreamHandler<StreamMessage, StreamResponse>
{
    public async IAsyncEnumerable<StreamResponse> Handle(StreamMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (message.Exception is not null)
        {
            yield return new StreamResponse(string.Empty);

            throw message.Exception;
        }

        message.Called = true;
        yield return new StreamResponse("ok");
        await Task.Yield();
        yield return new StreamResponse("done");
    }
}

[DecoratorFor<ICommandHandler<CommandMessage>>]
internal sealed class CommandActivityBehavior(ICommandHandler<CommandMessage> handler) : TelemetryCommandBehavior<CommandMessage>(handler);

[DecoratorFor<INotificationHandler<NotificationMessage>>]
internal sealed class NotificationActivityBehavior(INotificationHandler<NotificationMessage> handler) : TelemetryNotificationBehavior<NotificationMessage>(handler);

[DecoratorFor<IRequestHandler<RequestMessage, Response>>]
internal sealed class RequestActivityBehavior(IRequestHandler<RequestMessage, Response> handler) : TelemetryRequestBehavior<RequestMessage, Response>(handler);

[DecoratorFor<IStreamHandler<StreamMessage, StreamResponse>>]
internal sealed class StreamActivityBehavior(IStreamHandler<StreamMessage, StreamResponse> handler) : TelemetryStreamBehavior<StreamMessage, StreamResponse>(handler);
