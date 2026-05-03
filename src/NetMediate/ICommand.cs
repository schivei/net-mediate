namespace NetMediate;

/// <summary>
/// Represents a command message that can be sent for processing within a messaging or mediator framework.
/// </summary>
/// <remarks>Commands represent one-way requests to perform an action or change state. Implement this interface
/// to define custom command types for use with mediator or messaging patterns.</remarks>
public interface ICommand : IMessage;
