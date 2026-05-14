namespace NetMediate;

/// <summary>
/// A dedicated shorthand for notification-specific pipeline behaviors, mirroring the symmetric
/// registration experience of <see cref="IPipelineRequestBehavior{TMessage,TResponse}"/> for requests.
/// </summary>
/// <remarks>
/// Implementations of this interface are registered via
/// <c>IMediatorServiceBuilder.RegisterNotificationBehavior&lt;TBehavior,TMessage&gt;()</c> and are resolved
/// exclusively for notification pipelines by <c>NotificationPipelineExecutor&lt;TMessage&gt;</c>.
/// This provides a type-safe, AOT-compatible way to add cross-cutting concerns to notification dispatch
/// without registering against the more general <c>IPipelineCommandBehavior&lt;TMessage, Task&gt;</c>.
/// </remarks>
/// <typeparam name="TMessage">The notification message type. Cannot be null.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1133:Deprecated code should be removed", Justification = "Legacy API kept for binary compatibility.")]
[Obsolete("This delegate is deprecated. Use DecoratorForAttribute instead.", true)]
public interface IPipelineNotificationBehavior<TMessage> : IPipelineBehavior<TMessage, Task>
    where TMessage : notnull;
