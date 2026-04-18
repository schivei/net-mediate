using System.Collections.Concurrent;
using System.Reflection;

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
        .GetMethod(nameof(CreateStreamCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, Task<object?>>> s_sendWithResponseInvokers =
        new();

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, Task>> s_sendWithoutResponseInvokers =
        new();

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, Task>> s_publishInvokers =
        new();

    private static readonly ConcurrentDictionary<Type, Func<NetMediate.IMediator, object, CancellationToken, IAsyncEnumerable<object?>>> s_streamInvokers =
        new();

    private readonly NetMediate.IMediator _mediator = mediator;

    public async Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await Send((object)request, cancellationToken);
        return response is null
            ? throw new InvalidOperationException("The request returned a null response.")
            : (TResponse)response;
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendWithoutResponse(_mediator, request, cancellationToken);
    }

    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var requestInterface = requestType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

        if (requestInterface is null)
        {
            if (request is IRequest)
            {
                await SendWithoutResponse(_mediator, request, cancellationToken);
                return null;
            }

            throw new ArgumentException(
                $"The object '{requestType.FullName}' does not implement IRequest.",
                nameof(request)
            );
        }

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
        ArgumentNullException.ThrowIfNull(request);

        return CreateStream(request, cancellationToken).Select(item => (TResponse)item!);
    }

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var requestInterface = requestType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamRequest<>));

        if (requestInterface is null)
        {
            throw new ArgumentException(
                $"The object '{requestType.FullName}' does not implement IStreamRequest<TResponse>.",
                nameof(request)
            );
        }

        return CreateStreamCore(
            _mediator,
            request,
            requestType,
            requestInterface.GetGenericArguments()[0],
            cancellationToken
        );
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification is not INotification)
        {
            throw new ArgumentException(
                $"The object '{notification.GetType().FullName}' does not implement INotification.",
                nameof(notification)
            );
        }

        return Publish(_mediator, notification, cancellationToken);
    }

    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default
    ) where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        return Publish((object)notification, cancellationToken);
    }

    private static Task<object?> SendWithResponse(
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
                return (innerMediator, message, token) =>
                    (Task<object?>)method.Invoke(null, [innerMediator, message, token])!;
            }
        )(mediator, request, cancellationToken);

    private static Task SendWithoutResponse(
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
                return (innerMediator, message, token) =>
                    (Task)method.Invoke(null, [innerMediator, message, token])!;
            }
        )(mediator, request, cancellationToken);
    }

    private static Task Publish(
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
                    (Task)method.Invoke(null, [innerMediator, message, token])!;
            }
        )(mediator, notification, cancellationToken);
    }

    private static IAsyncEnumerable<object?> CreateStreamCore(
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

    private static async Task<object?> SendWithResponseCore<TRequest, TResponse>(
        NetMediate.IMediator mediator,
        object request,
        CancellationToken cancellationToken
    )
    {
        var response = await mediator.Request<TRequest, TResponse>(
            (TRequest)request,
            cancellationToken
        );
        return response;
    }

    private static Task SendWithoutResponseCore<TRequest>(
        NetMediate.IMediator mediator,
        object request,
        CancellationToken cancellationToken
    ) => mediator.Send((TRequest)request, cancellationToken);

    private static Task PublishCore<TNotification>(
        NetMediate.IMediator mediator,
        object notification,
        CancellationToken cancellationToken
    ) => mediator.Notify((TNotification)notification, cancellationToken);

    private static async IAsyncEnumerable<object?> CreateStreamCore<TRequest, TResponse>(
        NetMediate.IMediator mediator,
        object request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken
    )
    {
        await foreach (var item in mediator.RequestStream<TRequest, TResponse>(
                           (TRequest)request,
                           cancellationToken
                       ))
        {
            yield return item;
        }
    }
}
