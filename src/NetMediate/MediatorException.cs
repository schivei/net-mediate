using System.Diagnostics;

namespace NetMediate;

/// <summary>
/// Represents an exception thrown when a mediator handler or pipeline behavior fails,
/// carrying structured context about the originating message type, handler type, and
/// distributed-tracing activity ID.
/// </summary>
/// <remarks>
/// <para>
/// Instances of this exception are created automatically by the mediator when a handler
/// or behavior throws an unhandled exception during <see cref="IMediator.Send{TMessage}(TMessage,CancellationToken)"/>
/// or <c>Request</c> calls.  The original exception
/// is preserved as <see cref="Exception.InnerException"/>.
/// </para>
/// <para>
/// Notification dispatch is fire-and-forget; handler exceptions there are logged rather
/// than surfaced as <see cref="MediatorException"/>.
/// </para>
/// </remarks>
public sealed class MediatorException : Exception
{
    /// <summary>Gets the CLR type of the message that was being dispatched when the exception occurred.</summary>
    public Type MessageType { get; }

    /// <summary>
    /// Gets the CLR type of the handler interface for which dispatch was attempted
    /// (e.g. <c>ICommandHandler&lt;TMessage&gt;</c>, <c>IRequestHandler&lt;TMessage,TResponse&gt;</c>).
    /// May be <see langword="null"/> when the handler type cannot be determined.
    /// </summary>
    public Type? HandlerType { get; }

    /// <summary>
    /// Gets the <see cref="Activity.Id"/> of the ambient <see cref="Activity"/> at the time
    /// the exception was captured, or <see langword="null"/> when no activity was active.
    /// </summary>
    public string? TraceId { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="MediatorException"/>.
    /// </summary>
    /// <param name="messageType">The message type being dispatched.</param>
    /// <param name="handlerType">The handler interface type, or <see langword="null"/>.</param>
    /// <param name="traceId">The activity trace ID, or <see langword="null"/>.</param>
    /// <param name="innerException">The original exception thrown by the handler or behavior.</param>
    public MediatorException(
        Type messageType,
        Type? handlerType,
        string? traceId,
        Exception innerException
    )
        : base(BuildMessage(messageType, handlerType), innerException)
    {
        MessageType = messageType;
        HandlerType = handlerType;
        TraceId = traceId;
    }

    private static string BuildMessage(Type messageType, Type? handlerType) =>
        handlerType is null
            ? $"A handler for '{messageType.Name}' failed."
            : $"A '{handlerType.Name}' handler for '{messageType.Name}' failed.";
}
