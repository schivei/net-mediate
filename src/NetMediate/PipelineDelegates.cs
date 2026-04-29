namespace NetMediate;

/// <summary>
/// Represents the next command delegate in the command pipeline.
/// </summary>
/// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
/// <returns>A task that represents the asynchronous operation.</returns>
public delegate Task CommandHandlerDelegate(CancellationToken cancellationToken);

/// <summary>
/// Represents the next request delegate in the request pipeline.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
/// <returns>A task containing the handler response.</returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

/// <summary>
/// Represents the next notification delegate in the notification pipeline.
/// </summary>
/// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
/// <returns>A task that represents the asynchronous operation.</returns>
public delegate Task NotificationHandlerDelegate(CancellationToken cancellationToken);

/// <summary>
/// Represents the next stream delegate in the stream pipeline.
/// </summary>
/// <typeparam name="TResponse">The stream item type.</typeparam>
/// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
/// <returns>An asynchronous stream of items.</returns>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>(
    CancellationToken cancellationToken
);
