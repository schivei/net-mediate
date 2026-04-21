namespace NetMediate;

/// <summary>
/// Defines a pipeline behavior for stream requests.
/// </summary>
/// <typeparam name="TMessage">The stream request message type.</typeparam>
/// <typeparam name="TResponse">The stream item type.</typeparam>
public interface IStreamBehavior<in TMessage, TResponse>
{
    /// <summary>
    /// Handles a stream request before and/or after invoking the next delegate in the pipeline.
    /// </summary>
    /// <param name="message">The stream request message.</param>
    /// <param name="next">The next delegate in the stream pipeline.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>An asynchronous stream of responses.</returns>
    IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default
    );
}
