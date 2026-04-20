namespace NetMediate;

/// <summary>
/// Defines a pipeline behavior for request messages.
/// </summary>
/// <typeparam name="TMessage">The request message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestBehavior<in TMessage, TResponse>
{
    /// <summary>
    /// Handles a request before and/or after invoking the next delegate in the pipeline.
    /// </summary>
    /// <param name="message">The request message.</param>
    /// <param name="next">The next delegate in the request pipeline.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task containing the response.</returns>
    Task<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default
    );
}
