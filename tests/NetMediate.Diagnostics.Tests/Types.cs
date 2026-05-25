using Microsoft.Extensions.DependencyInjection;
using System.Collections;
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

internal sealed class TestServiceCollection : IServiceCollection
{
    private readonly List<ServiceDescriptor> _descriptors = [];

    private readonly Lock _lock = new();

    public ServiceDescriptor this[int index]
    {
        get
        {
            lock(_lock)
            {
                return _descriptors[index];
            }
        }
        set
        {
            lock(_lock)
            {
                _descriptors[index] = value;
            }
        }
    }

    public int Count => _descriptors.Count;

    public bool IsReadOnly => false;

    public void Add(ServiceDescriptor item)
    {
        lock(_lock)
        {
            _descriptors.Add(item);
        }
    }

    public void Clear()
    {
        lock(_lock)
        {
            _descriptors.Clear();
        }
    }

    public bool Contains(ServiceDescriptor item)
    {
        lock(_lock)
        {
            return _descriptors.Contains(item);
        }
    }

    public void CopyTo(ServiceDescriptor[] array, int arrayIndex)
    {
        lock(_lock)
        {
            _descriptors.CopyTo(array, arrayIndex);
        }
    }

    public IEnumerator<ServiceDescriptor> GetEnumerator()
    {
        lock(_lock)
        {
            return _descriptors.GetEnumerator();
        }
    }

    public int IndexOf(ServiceDescriptor item)
    {
        lock(_lock)
        {
            return _descriptors.IndexOf(item);
        }
    }

    public void Insert(int index, ServiceDescriptor item)
    {
        lock(_lock)
        {
            _descriptors.Insert(index, item);
        }
    }

    public bool Remove(ServiceDescriptor item)
    {
        lock(_lock)
        {
            return _descriptors.Remove(item);
        }
    }

    public void RemoveAt(int index)
    {
        lock(_lock)
        {
            _descriptors.RemoveAt(index);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
