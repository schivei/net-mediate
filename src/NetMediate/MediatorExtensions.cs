using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NetMediate;

/// <summary>
/// Provides extension methods for the IMediator interface to facilitate sending request and streaming messages.
/// </summary>
/// <remarks>These extension methods offer a simplified way to send requests and initiate streaming operations
/// using an IMediator instance. They support both standard request/response and streaming scenarios, allowing for
/// asynchronous and cancellable operations.</remarks>
[RequiresDynamicCode(
    "Generic dispatch uses MakeGenericMethod and is not compatible with NativeAOT. " +
    "Use IMediator.Request<TMessage, TResponse>() instead."
)]
[RequiresUnreferencedCode(
    "Generic dispatch uses reflection to construct the method invocation. " +
    "Use IMediator.Request<TMessage, TResponse>() instead."
)]
[ExcludeFromCodeCoverage]
public static class MediatorExtensions
{
    private static readonly MethodInfo? s_requestMethod = typeof(IMediator)
        .GetMethods()
        .FirstOrDefault(m =>
            m.Name == nameof(IMediator.Request) &&
            m.IsGenericMethodDefinition &&
            m.GetGenericArguments().Length == 2 &&
            m.GetParameters() is var p &&
            p.Length == 3 &&
            p[0].ParameterType == typeof(object));

    private static readonly MethodInfo? s_requestStreamMethod = typeof(IMediator)
        .GetMethods()
        .FirstOrDefault(m =>
            m.Name == nameof(IMediator.RequestStream) &&
            m.IsGenericMethodDefinition &&
            m.GetGenericArguments().Length == 2 &&
            m.GetParameters() is var p &&
            p.Length == 3 &&
            p[0].ParameterType == typeof(object));

    extension (IMediator mediator)
    {
        /// <summary>
        /// Sends a request to a handler and awaits a response.
        /// </summary>
        /// <remarks>
        /// This method uses reflection (<see cref="MethodInfo.MakeGenericMethod"/>) internally to
        /// dispatch the request. It is not compatible with NativeAOT or trimming. Prefer the
        /// IMediator.Request{TMessage, TResponse} overload which specifies both type
        /// arguments explicitly.
        /// </remarks>
        /// <typeparam name="TResponse">The response type.</typeparam>
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
        public Task<TResponse> Request<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            RequestInvoker<TResponse>.Invoke(null, mediator, request, cancellationToken);

        /// <summary>
        /// Sends a request to a handler and awaits a response.
        /// </summary>
        /// <remarks>
        /// This method uses reflection (<see cref="MethodInfo.MakeGenericMethod"/>) internally to
        /// dispatch the request. It is not compatible with NativeAOT or trimming. Prefer the
        /// IMediator.Request{TMessage, TResponse} overload which specifies both type
        /// arguments explicitly.
        /// </remarks>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="key">An optional key to distinguish this request from others of the same message type.</param>
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
        public Task<TResponse> Request<TResponse>(object? key, IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            RequestInvoker<TResponse>.Invoke(key, mediator, request, cancellationToken);

        /// <summary>
        /// Sends a request to a handler and receives a stream of responses asynchronously.
        /// </summary>
        /// <remarks>
        /// This method uses reflection (<see cref="MethodInfo.MakeGenericMethod"/>) internally to
        /// dispatch the stream request. It is not compatible with NativeAOT or trimming. Prefer the
        /// IMediator.RequestStream{TMessage, TResponse} overload which specifies both
        /// type arguments explicitly.
        /// </remarks>
        /// <typeparam name="TResponse">The response item type.</typeparam>
        /// <param name="message">Stream request instance.</param>
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
        public IAsyncEnumerable<TResponse> RequestStream<TResponse>(IStream<TResponse> message, CancellationToken cancellationToken = default) =>
            RequestStreamInvoker<TResponse>.Invoke(null, mediator, message, cancellationToken);

        /// <summary>
        /// Sends a request to a handler and receives a stream of responses asynchronously.
        /// </summary>
        /// <remarks>
        /// This method uses reflection (<see cref="MethodInfo.MakeGenericMethod"/>) internally to
        /// dispatch the stream request. It is not compatible with NativeAOT or trimming. Prefer the
        /// IMediator.RequestStream{TMessage, TResponse} overload which specifies both
        /// type arguments explicitly.
        /// </remarks>
        /// <typeparam name="TResponse">The response item type.</typeparam>
        /// <param name="key">An optional key to distinguish this request from others of the same message type.</param>
        /// <param name="message">Stream request instance.</param>
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
        public IAsyncEnumerable<TResponse> RequestStream<TResponse>(object? key, IStream<TResponse> message, CancellationToken cancellationToken = default) =>
            RequestStreamInvoker<TResponse>.Invoke(key, mediator, message, cancellationToken);
    }

    [RequiresDynamicCode("Generic dispatch uses MakeGenericMethod.")]
    [RequiresUnreferencedCode("Generic dispatch uses reflection.")]
    private static class RequestInvoker<TResponse>
    {
        private static readonly ConcurrentDictionary<Type, Func<IMediator, object?, object, CancellationToken, Task<TResponse>>> s_cache =
            new();

        public static Task<TResponse> Invoke(
            object? key,
            IMediator mediator,
            IRequest<TResponse> request,
            CancellationToken cancellationToken
        ) => s_cache.GetOrAdd(request.GetType(), Create)(mediator, key, request, cancellationToken);

        private static Func<IMediator, object?, object, CancellationToken, Task<TResponse>> Create(Type requestType)
        {
            if (s_requestMethod is null)
                throw new InvalidOperationException("IMediator.Request method not found via reflection.");

            var method = s_requestMethod.MakeGenericMethod(requestType, typeof(TResponse));
            return (mediator, key, request, cancellationToken) =>
            {
                var result = method.Invoke(mediator, [key, request, cancellationToken]) ??
                    throw new InvalidOperationException("Request method invocation returned null.");

                return (Task<TResponse>)result;
            };
        }
    }

    [RequiresDynamicCode("Generic dispatch uses MakeGenericMethod.")]
    [RequiresUnreferencedCode("Generic dispatch uses reflection.")]
    private static class RequestStreamInvoker<TResponse>
    {
        private static readonly ConcurrentDictionary<Type, Func<IMediator, object?, object, CancellationToken, IAsyncEnumerable<TResponse>>> s_cache =
            new();

        public static IAsyncEnumerable<TResponse> Invoke(
            object? key,
            IMediator mediator,
            IStream<TResponse> request,
            CancellationToken cancellationToken
        ) => s_cache.GetOrAdd(request.GetType(), Create)(mediator, key, request, cancellationToken);

        private static Func<IMediator, object?, object, CancellationToken, IAsyncEnumerable<TResponse>> Create(Type requestType)
        {
            if (s_requestStreamMethod is null)
                throw new InvalidOperationException("IMediator.RequestStream method not found via reflection.");

            var method = s_requestStreamMethod.MakeGenericMethod(requestType, typeof(TResponse));
            return (mediator, key, request, cancellationToken) =>
            {
                var result = method.Invoke(mediator, [key, request, cancellationToken]) ??
                    throw new InvalidOperationException("RequestStream method invocation returned null.");

                return (IAsyncEnumerable<TResponse>)result;
            };
        }
    }
}
