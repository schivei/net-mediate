namespace NetMediate;

/// <summary>
/// Represents a command that can be executed within an application.
/// </summary>
/// <remarks>Implement this interface to define operations or actions that encapsulate a request as an object.
/// This pattern is commonly used to decouple the sender of a request from its handler, enabling features such as
/// undo/redo, logging, or queuing of operations.</remarks>
public interface ICommand : IMessage;
