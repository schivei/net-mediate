namespace NetMediate;

/// <summary>
/// Represents a command message that can be sent for processing within a messaging or mediator framework.
/// </summary>
/// <remarks>Commands typically represent requests to perform an action or change state, and are handled by a
/// single handler. Implement this interface to define custom command types for use with mediator or messaging
/// patterns.</remarks>
public interface ICommand : IMessage;
