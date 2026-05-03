namespace NetMediate;

/// <summary>
/// Provides convenient extension methods over <see cref="IMediator"/> that leverage the
/// <see cref="IRequest{TResponse}"/> and <see cref="IStream{TResponse}"/> marker interfaces to infer the
/// response type automatically.
/// </summary>
public static class MediatorExtensions
{
    /// <summary>
    /// Sends a request to a handler and awaits a response, inferring the response type from the
    /// <see cref="IRequest{TResponse}"/> marker interface implemented by <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The concrete request message type. Must implement <see cref="IRequest{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The type of the response, inferred from the <see cref="IRequest{TResponse}"/> constraint.</typeparam>
    /// <param name="mediator">The mediator instance.</param>
    /// <param name="request">The request message to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task representing the asynchronous operation that returns the handler response.</returns>
    public static Task<TResponse> Request<TMessage, TResponse>(
        this IMediator mediator,
        TMessage request,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull, IRequest<TResponse>
        => ((IMediator)mediator).Request<TMessage, TResponse>(request, cancellationToken);

    /// <summary>
    /// Sends a request to a stream handler and returns an asynchronous sequence of responses, inferring the
    /// response type from the <see cref="IStream{TResponse}"/> marker interface implemented by <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The concrete stream request message type. Must implement <see cref="IStream{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The type of each response item in the stream, inferred from the <see cref="IStream{TResponse}"/> constraint.</typeparam>
    /// <param name="mediator">The mediator instance.</param>
    /// <param name="request">The stream request message.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the stream to begin.</param>
    /// <returns>An asynchronous sequence of response items produced by the handler.</returns>
    public static IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        this IMediator mediator,
        TMessage request,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull, IStream<TResponse>
        => ((IMediator)mediator).RequestStream<TMessage, TResponse>(request, cancellationToken);
}
