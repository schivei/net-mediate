namespace NetMediate;

/// <inheritdoc/>
public abstract class ABaseHandler<TMessage, TResult> : IHandler<TMessage, TResult>
{
    /// <inheritdoc/>
    public abstract TResult Handle(TMessage message, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public object Handle(object message, CancellationToken cancellationToken = default) =>
        Handle((TMessage)message, cancellationToken);
}
