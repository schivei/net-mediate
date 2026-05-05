---
sidebar_position: 3
---

# Behavior Interfaces

## IPipelineBehavior<TMessage, TResult>

Generic pipeline behavior interface.

```csharp
public interface IPipelineBehavior<in TMessage, TResult>
{
    Task<TResult> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, TResult> next,
        CancellationToken cancellationToken);
}
```

## IPipelineRequestBehavior<TMessage, TResponse>

Behavior for request pipeline.

## IPipelineNotificationBehavior<TMessage>

Behavior for notification pipeline.

## IPipelineStreamBehavior<TMessage, TResponse>

Behavior for stream pipeline.
