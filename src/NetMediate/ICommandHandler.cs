namespace NetMediate;

/// <summary>
/// Defines a handler for processing a command message asynchronously.
/// </summary>
/// <remarks>Implement this interface to provide custom logic for handling command messages. The handler executes
/// the command and returns a <see cref="ValueTask"/> representing the asynchronous operation.</remarks>
/// <typeparam name="TMessage">The type of command message to handle. Must implement <see cref="ICommand"/> and cannot be null.</typeparam>
public interface ICommandHandler<TMessage> : IHandler<TMessage, ValueTask> where TMessage : notnull, ICommand;
