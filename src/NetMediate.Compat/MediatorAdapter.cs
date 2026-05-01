using System.Collections.Concurrent;
using System.Reflection;
using NetMediate;

namespace MediatR;

internal sealed class MediatorAdapter(NetMediate.IMediator mediator) : IMediator
{
    private static readonly MethodInfo s_sendWithResponseMethod = typeof(MediatorAdapter)
        .GetMethod(nameof(SendWithResponseCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo s_sendWithoutResponseMethod = typeof(MediatorAdapter)
        .GetMethod(nameof(SendWithoutResponseCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo s_publishMethod = typeof(MediatorAdapter)
        .GetMethod(nameof(PublishCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo s_streamMethod = typeof(MediatorAdapter)
        .GetMethod(nameof(CreateStreamInvokerCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, ValueTask<object?>>> s_sendWithResponseInvokers =
        new();

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, ValueTask>> s_sendWithoutResponseInvokers =
        new();

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, ValueTask>> s_publishInvokers =
        new();

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, IAsyncEnumerable<object?>>> s_streamInvokers =
        new();

    private readonly NetMediate.IMediator _mediator = mediator;

    public async ValueTask<TResponse?> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        Guard.ThrowIfNull(request);

        var response = await Send((object)request, cancellationToken);
        return response is null
            ? throw new InvalidOperationException("The request returned a null response.")
            : (TResponse)response;
    }

    public ValueTask Send(IRequest request, CancellationToken cancellationToken = default)
    {
        Guard.ThrowIfNull(request);

        return SendWithoutResponse(_mediator, request, cancellationToken);
    }

    public async ValueTask<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        Guard.ThrowIfNull(request);

        if (request is IRequest)
        {
            await SendWithoutResponse(_mediator, request, cancellationToken);
            return Unit.Value;
        }

        var requestType = request.GetType();
        var requestInterface = requestType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)) ?? throw new ArgumentException(
                $"The object '{requestType.FullName}' does not implement IRequest.",
                nameof(request)
            );

        return await SendWithResponse(
            _mediator,
            request,
            requestType,
            requestInterface.GetGenericArguments()[0],
            cancellationToken
        );
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        Guard.ThrowIfNull(request);

        return CreateTypedStream(request, cancellationToken);
    }

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default
    )
    {
        Guard.ThrowIfNull(request);

        var requestType = request.GetType();
        var requestInterface = requestType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamRequest<>)) ?? throw new ArgumentException(
                $"The object '{requestType.FullName}' does not implement IStreamRequest<TResponse>.",
                nameof(request)
            );

        return CreateStreamInvoker(
            _mediator,
            request,
            requestType,
            requestInterface.GetGenericArguments()[0],
            cancellationToken
        );
    }

    public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
    {
        Guard.ThrowIfNull(notification);

        if (notification is not INotification)
        {
            throw new ArgumentException(
                $"The object '{notification.GetType().FullName}' does not implement INotification.",
                nameof(notification)
            );
        }

        return Publish(_mediator, notification, cancellationToken);
    }

    public ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default
    ) where TNotification : INotification
    {
        Guard.ThrowIfNull(notification);

        return Publish((object)notification, cancellationToken);
    }

    private static ValueTask<object?> SendWithResponse(
        NetMediate.IMediator mediator,
        object request,
        Type requestType,
        Type responseType,
        CancellationToken cancellationToken
    ) =>
        s_sendWithResponseInvokers.GetOrAdd(
            requestType,
            _ =>
            {
                var method = s_sendWithResponseMethod.MakeGenericMethod(requestType, responseType);
                return (innerMediator, message, token) => FromValue(method.Invoke(null, [innerMediator, message, token]));
            }
        )(mediator, request, cancellationToken);

    private static async ValueTask<object?> FromValue(object? unknownInvokeResult)
    {
        if (unknownInvokeResult == null)
        {
            return null;
        }

        var invokeResultType = unknownInvokeResult.GetType();

        if (invokeResultType == typeof(void))
        {
            return null;
        }

        if (invokeResultType.IsGenericType && invokeResultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultProperty = invokeResultType.GetProperty("Result")!;
            await (ValueTask)unknownInvokeResult;
            return resultProperty.GetValue(unknownInvokeResult);
        }

        if (invokeResultType == typeof(ValueTask))
        {
            await (ValueTask)unknownInvokeResult;
            return null;
        }

        throw new InvalidOperationException(
            $"Unexpected return type from handler invocation: {invokeResultType.FullName}. Expected ValueTask or ValueTask<T>."
        );
    }

    private static ValueTask SendWithoutResponse(
        NetMediate.IMediator mediator,
        object request,
        CancellationToken cancellationToken
    )
    {
        var requestType = request.GetType();

        return s_sendWithoutResponseInvokers.GetOrAdd(
            requestType,
            static type =>
            {
                var method = s_sendWithoutResponseMethod.MakeGenericMethod(type);
                return (innerMediator, message, token) => (ValueTask)method.Invoke(null, [innerMediator, message, token])!;
            }
        )(mediator, request, cancellationToken);
    }

    private static ValueTask Publish(
        NetMediate.IMediator mediator,
        object notification,
        CancellationToken cancellationToken
    )
    {
        var notificationType = notification.GetType();

        return s_publishInvokers.GetOrAdd(
            notificationType,
            static type =>
            {
                var method = s_publishMethod.MakeGenericMethod(type);
                return (innerMediator, message, token) =>
                    (ValueTask)method.Invoke(null, [innerMediator, message, token])!;
            }
        )(mediator, notification, cancellationToken);
    }

    private static IAsyncEnumerable<object?> CreateStreamInvoker(
        NetMediate.IMediator mediator,
        object request,
        Type requestType,
        Type responseType,
        CancellationToken cancellationToken
    ) =>
        s_streamInvokers.GetOrAdd(
            requestType,
            _ =>
            {
                var method = s_streamMethod.MakeGenericMethod(requestType, responseType);
                return (innerMediator, message, token) =>
                    (IAsyncEnumerable<object?>)method.Invoke(null, [innerMediator, message, token])!;
            }
        )(mediator, request, cancellationToken);

    private static async ValueTask<object?> SendWithResponseCore<TRequest, TResponse>(
        NetMediate.IMediator mediator,
        object request,
        CancellationToken cancellationToken
    ) where TRequest : IRequest<TResponse>
    {
        var response = await mediator.Request<TRequest, TResponse>(
            (TRequest)request,
            cancellationToken
        );
        return response;
    }

    private static ValueTask SendWithoutResponseCore<TRequest>(
        NetMediate.IMediator mediator,
        object request,
        CancellationToken cancellationToken
    ) where TRequest : IRequest =>
        mediator.Send((TRequest)request, cancellationToken);

    private static ValueTask PublishCore<TNotification>(
        NetMediate.IMediator mediator,
        object notification,
        CancellationToken cancellationToken
    ) where TNotification : INotification =>
        mediator.Notify((TNotification)notification, cancellationToken);

    private static async IAsyncEnumerable<object?> CreateStreamInvokerCore<TRequest, TResponse>(
        NetMediate.IMediator mediator,
        object request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken
    ) where TRequest : IStreamRequest<TResponse>
    {
        await foreach (var item in mediator.RequestStream<TRequest, TResponse>(
                           (TRequest)request,
                           cancellationToken
                       ))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<TResponse> CreateTypedStream<TResponse>(
        IStreamRequest<TResponse> request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in CreateStream((object)request, cancellationToken))
            yield return (TResponse)item!;
    }
}
