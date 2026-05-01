using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NetMediate;

/// <summary>
/// Provides convenience overloads for mediator operations that allow response type inference.
/// </summary>
public static class MediatorExtensions
{
    private static readonly MethodInfo? s_requestMethod = typeof(IMediator)
        .GetMethod(nameof(IMediator.Request));

    private static readonly MethodInfo? s_requestStreamMethod = typeof(IMediator)
        .GetMethod(nameof(IMediator.RequestStream));

    /// <summary>
    /// Sends a request to a handler and awaits a response.
    /// </summary>
    /// <remarks>
    /// This method uses reflection (<see cref="MethodInfo.MakeGenericMethod"/>) internally to
    /// dispatch the request. It is not compatible with NativeAOT or trimming. Prefer the
    /// <see cref="IMediator.Request{TMessage, TResponse}"/> overload which specifies both type
    /// arguments explicitly.
    /// </remarks>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="mediator">Mediator instance.</param>
    /// <param name="request">Request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response.</returns>
    [RequiresDynamicCode(
        "Generic dispatch uses MakeGenericMethod and is not compatible with NativeAOT. " +
        "Use IMediator.Request<TMessage, TResponse>() instead."
    )]
    [RequiresUnreferencedCode(
        "Generic dispatch uses reflection to construct the method invocation. " +
        "Use IMediator.Request<TMessage, TResponse>() instead."
    )]
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
    /// <remarks>
    /// This method uses reflection (<see cref="MethodInfo.MakeGenericMethod"/>) internally to
    /// dispatch the stream request. It is not compatible with NativeAOT or trimming. Prefer the
    /// <see cref="IMediator.RequestStream{TMessage, TResponse}"/> overload which specifies both
    /// type arguments explicitly.
    /// </remarks>
    /// <typeparam name="TResponse">The response item type.</typeparam>
    /// <param name="mediator">Mediator instance.</param>
    /// <param name="request">Stream request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An asynchronous response stream.</returns>
    [RequiresDynamicCode(
        "Generic dispatch uses MakeGenericMethod and is not compatible with NativeAOT. " +
        "Use IMediator.RequestStream<TMessage, TResponse>() instead."
    )]
    [RequiresUnreferencedCode(
        "Generic dispatch uses reflection to construct the method invocation. " +
        "Use IMediator.RequestStream<TMessage, TResponse>() instead."
    )]
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

    [RequiresDynamicCode("Generic dispatch uses MakeGenericMethod.")]
    [RequiresUnreferencedCode("Generic dispatch uses reflection.")]
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
            if (s_requestMethod is null)
                throw new InvalidOperationException("IMediator.Request method not found via reflection.");

            var method = s_requestMethod.MakeGenericMethod(requestType, typeof(TResponse));
            return (mediator, request, cancellationToken) =>
            {
                var result = method.Invoke(mediator, [request, cancellationToken]);
                if (result is null)
                    throw new InvalidOperationException("Request method invocation returned null.");
                return (ValueTask<TResponse>)result;
            };
        }
    }

    [RequiresDynamicCode("Generic dispatch uses MakeGenericMethod.")]
    [RequiresUnreferencedCode("Generic dispatch uses reflection.")]
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
            if (s_requestStreamMethod is null)
                throw new InvalidOperationException("IMediator.RequestStream method not found via reflection.");

            var method = s_requestStreamMethod.MakeGenericMethod(requestType, typeof(TResponse));
            return (mediator, request, cancellationToken) =>
            {
                var result = method.Invoke(mediator, [request, cancellationToken]);
                if (result is null)
                    throw new InvalidOperationException("RequestStream method invocation returned null.");
                return (IAsyncEnumerable<TResponse>)result;
            };
        }
    }
}
