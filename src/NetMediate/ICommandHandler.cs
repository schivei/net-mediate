namespace NetMediate;

/// <summary>
/// Defines a handler for processing a command message asynchronously.
/// </summary>
/// <remarks>Implement this interface to provide custom logic for handling command messages. The handler executes
/// the command and returns a <see cref="Task"/> representing the asynchronous operation.</remarks>
/// <typeparam name="TMessage">The type of command message to handle. Must not be null.</typeparam>
public interface ICommandHandler<in TMessage> : IHandler<TMessage, Task> where TMessage : notnull;
