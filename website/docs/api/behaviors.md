---
sidebar_position: 3
---

# Behavior Interfaces

## IPipelineBehavior&lt;TMessage, TResult&gt;

Generic pipeline behavior interface.

```csharp
public interface IPipelineBehavior<in TMessage, TResult>
    where TMessage : notnull
    where TResult : notnull
{
    TResult Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, TResult> next,
        CancellationToken cancellationToken);
}
```

## IPipelineRequestBehavior&lt;TMessage, TResponse&gt;

Behavior for request pipeline.

## IPipelineNotificationBehavior&lt;TMessage&gt;

Behavior for notification pipeline.

## IPipelineStreamBehavior&lt;TMessage, TResponse&gt;

Behavior for stream pipeline.
