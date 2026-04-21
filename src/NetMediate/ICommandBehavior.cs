namespace NetMediate;

/// <summary>
/// Defines a pipeline behavior for command messages.
/// </summary>
/// <typeparam name="TMessage">The command message type.</typeparam>
public interface ICommandBehavior<in TMessage>
{
    /// <summary>
    /// Handles a command before and/or after invoking the next delegate in the pipeline.
    /// </summary>
    /// <param name="message">The command message.</param>
    /// <param name="next">The next delegate in the command pipeline.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Handle(
        TMessage message,
        CommandHandlerDelegate next,
        CancellationToken cancellationToken = default
    );
}
