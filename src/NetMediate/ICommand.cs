namespace NetMediate;

/// <summary>
/// Represents a command message that can be sent for processing within a messaging or mediator framework.
/// </summary>
/// <remarks>Commands represent one-way requests to perform an action or change state. When sent via
/// <see cref="IMediator.Send{TMessage}"/>, the command is dispatched to <em>all</em> registered
/// <see cref="ICommandHandler{TMessage}"/> implementations in parallel (using <c>Task.WhenAll</c>).
/// Implement this interface to define custom command types for use with mediator or messaging patterns.</remarks>
public interface ICommand : IMessage;
