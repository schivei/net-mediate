using GenDI;

namespace NetMediate;

/// <summary>
/// Defines a behavior that can be added to the processing pipeline for a message, allowing custom logic to be executed
/// before or after the main message handler.
/// </summary>
/// <remarks>Implementations of this interface can be used to add cross-cutting concerns, such as logging,
/// validation, or exception handling, to the message processing pipeline. The behavior can invoke the next delegate to
/// continue processing or short-circuit the pipeline as needed.</remarks>
/// <typeparam name="TMessage">The type of the message being processed. Must implement the IMessage interface and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the pipeline after processing the message.</typeparam>
[ServiceInjection]
public interface IPipelineRequestBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, Task<TResponse>>
    where TMessage : notnull;
