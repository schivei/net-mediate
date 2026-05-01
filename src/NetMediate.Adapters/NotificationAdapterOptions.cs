namespace NetMediate.Adapters;

/// <summary>
/// Configuration options for <see cref="NotificationAdapterBehavior{TMessage}"/>.
/// </summary>
public sealed class NotificationAdapterOptions
{
    /// <summary>
    /// Gets or sets whether adapter failures should propagate and cancel the notification pipeline.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/> (default), an exception thrown by an adapter will propagate to the caller.
    /// When <see langword="false"/>, exceptions from adapters are swallowed and the pipeline continues.
    /// </remarks>
    public bool ThrowOnAdapterFailure { get; set; } = true;

    /// <summary>
    /// Gets or sets whether adapters should be invoked in parallel (simultaneously) or sequentially.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/> (default), adapters for a given message type are called one after another
    /// in registration order. Set to <see langword="true"/> to fire all adapters concurrently with
    /// <c>Task.WhenAll</c>.
    /// </remarks>
    public bool InvokeAdaptersInParallel { get; set; }
}
