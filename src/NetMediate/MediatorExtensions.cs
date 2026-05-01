using System.Collections.Concurrent;
using System.Reflection;

namespace NetMediate;

/// <summary>
/// Provides convenience overloads for mediator operations that allow response type inference.
/// </summary>
public static class MediatorExtensions
{
    private static readonly MethodInfo s_requestMethod = typeof(IMediator)
        .GetMethod(nameof(IMediator.Request));

    private static readonly MethodInfo s_requestStreamMethod = typeof(IMediator)
        .GetMethod(nameof(IMediator.RequestStream));

    /// <summary>
    /// Sends a request to a handler and awaits a response.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="mediator">Mediator instance.</param>
    /// <param name="request">Request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response.</returns>
    public static ValueTask<TResponse> Request<TResponse>(
        this IMediator mediator,
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        Guard.ThrowIfNull(mediator);
        Guard.ThrowIfNull(request);

        return RequestInvoker<TResponse>.Invoke(mediator, request, cancellationToken);
    }

    /// <summary>
    /// Sends a request to a handler and receives a stream of responses asynchronously.
    /// </summary>
    /// <typeparam name="TResponse">The response item type.</typeparam>
    /// <param name="mediator">Mediator instance.</param>
    /// <param name="request">Stream request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An asynchronous response stream.</returns>
    public static IAsyncEnumerable<TResponse> RequestStream<TResponse>(
        this IMediator mediator,
        IStream<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        Guard.ThrowIfNull(mediator);
        Guard.ThrowIfNull(request);

        return RequestStreamInvoker<TResponse>.Invoke(mediator, request, cancellationToken);
    }

    private static class RequestInvoker<TResponse>
    {
        private static readonly ConcurrentDictionary<Type, Func<IMediator, object, CancellationToken, ValueTask<TResponse>>> s_cache =
            new();

        public static ValueTask<TResponse> Invoke(
            IMediator mediator,
            IRequest<TResponse> request,
            CancellationToken cancellationToken
        ) => s_cache.GetOrAdd(request.GetType(), Create)(mediator, request, cancellationToken);

        private static Func<IMediator, object, CancellationToken, ValueTask<TResponse>> Create(Type requestType)
        {
            var method = s_requestMethod.MakeGenericMethod(requestType);
            return (mediator, request, cancellationToken) =>
                (ValueTask<TResponse>)method.Invoke(mediator, [request, cancellationToken])!;
        }
    }

    private static class RequestStreamInvoker<TResponse>
    {
        private static readonly ConcurrentDictionary<Type, Func<IMediator, object, CancellationToken, IAsyncEnumerable<TResponse>>> s_cache =
            new();

        public static IAsyncEnumerable<TResponse> Invoke(
            IMediator mediator,
            IStream<TResponse> request,
            CancellationToken cancellationToken
        ) => s_cache.GetOrAdd(request.GetType(), Create)(mediator, request, cancellationToken);

        private static Func<IMediator, object, CancellationToken, IAsyncEnumerable<TResponse>> Create(Type requestType)
        {
            var method = s_requestStreamMethod.MakeGenericMethod(requestType);
            return (mediator, request, cancellationToken) =>
                (IAsyncEnumerable<TResponse>)method.Invoke(mediator, [request, cancellationToken])!;
        }
    }
}
