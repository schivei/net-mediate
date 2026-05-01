namespace NetMediate;

/// <summary>
/// Defines a behavior that can be executed as part of the command handling pipeline for a specific command type.
/// </summary>
/// <remarks>Implement this interface to add custom logic before or after command handlers are invoked in the
/// pipeline. Behaviors can be used for cross-cutting concerns such as validation, logging, or transaction
/// management.</remarks>
/// <typeparam name="TMessage">The type of command message handled by the behavior. Must implement the ICommand interface and cannot be null.</typeparam>
public interface ICommandBehavior<TMessage> : IPipelineBehavior<TMessage, ValueTask, CommandHandlerDelegate<TMessage>> where TMessage : notnull, ICommand;
