using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;

namespace NetMediate;

[Injectable]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class CachedHandlers : ICachedHandlers
{
    [Inject] internal required IServiceProvider ServiceProvider { get; init; }

    private readonly ConcurrentDictionary<(Type, object?), Lazy<object>> _cmdCache = new();
    private readonly ConcurrentDictionary<(Type, object?), Lazy<object>> _ntfCache = new();
    private readonly ConcurrentDictionary<(Type, Type, object?), Lazy<object>> _reqCache = new();
    private readonly ConcurrentDictionary<(Type, Type, object?), Lazy<object>> _streamCache = new();

    public ImmutableArray<ICommandHandler<TMessage>> GetCommandHandlers<TMessage>(object? key)
        where TMessage : notnull =>
        (ImmutableArray<ICommandHandler<TMessage>>)(_cmdCache.GetOrAdd(
            (typeof(TMessage), key),
            k => new(() => k.Item2 is null ?
                ServiceProvider.GetServices<ICommandHandler<TMessage>>().ToImmutableArray() :
                [.. ServiceProvider.GetKeyedServices<ICommandHandler<TMessage>>(k.Item2)]
            ))).Value;

    public ImmutableArray<INotificationHandler<TMessage>> GetNotifyHandlers<TMessage>(object? key)
        where TMessage : notnull =>
        (ImmutableArray<INotificationHandler<TMessage>>)_ntfCache.GetOrAdd(
            (typeof(TMessage), key),
            k => new(() => k.Item2 is null ?
                ServiceProvider.GetServices<INotificationHandler<TMessage>>().ToImmutableArray() :
                [.. ServiceProvider.GetKeyedServices<INotificationHandler<TMessage>>(k.Item2)]
            )).Value;

    public IRequestHandler<TMessage, TResponse> GetRequestHandler<TMessage, TResponse>(object? key)
        where TMessage : notnull =>
        (IRequestHandler<TMessage, TResponse>)_reqCache.GetOrAdd(
            (typeof(TMessage), typeof(TResponse), key),
            k => new(() => k.Item3 is null ?
                ServiceProvider.GetRequiredService<IRequestHandler<TMessage, TResponse>>() :
                ServiceProvider.GetRequiredKeyedService<IRequestHandler<TMessage, TResponse>>(k.Item3)
            )).Value;

    public ImmutableArray<IStreamHandler<TMessage, TResponse>> GetStreamHandlers<TMessage, TResponse>(object? key)
        where TMessage : notnull =>
        (ImmutableArray<IStreamHandler<TMessage, TResponse>>)_streamCache.GetOrAdd(
            (typeof(TMessage), typeof(TResponse), key),
            k => new(() => k.Item3 is null ?
                ServiceProvider.GetServices<IStreamHandler<TMessage, TResponse>>().ToImmutableArray() :
                [.. ServiceProvider.GetKeyedServices<IStreamHandler<TMessage, TResponse>>(k.Item3)]
            )).Value;
}
