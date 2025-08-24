namespace NetMediate;

/// <summary>
/// Defines a handler for a command message.
/// </summary>
/// <typeparam name="TMessage">The type of the command message to handle.</typeparam>
public interface ICommandHandler<in TMessage> : IHandler
{
    /// <summary>
    /// Handles the specified command asynchronously.
    /// </summary>
    /// <param name="command">The command message to handle.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Handle(TMessage command, CancellationToken cancellationToken = default);
}
