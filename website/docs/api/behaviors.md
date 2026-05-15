---
sidebar_position: 3
---

# Behavior Interfaces

## Status

`IPipeline*Behavior` interfaces and pipeline delegates are obsolete and no longer supported for new implementations.
Use GenDI decorators with `DecoratorForAttribute`.

```csharp
[DecoratorFor<IRequestHandler<MyRequest, MyResponse>>(Order = 1)]
public sealed class MyRequestDecorator(IRequestHandler<MyRequest, MyResponse> inner)
    : IRequestHandler<MyRequest, MyResponse>
{
    public Task<MyResponse> Handle(MyRequest message, CancellationToken cancellationToken = default)
        => inner.Handle(message, cancellationToken);
}
```

## Obsolete contracts

- `IPipelineBehavior<TMessage, TResult>`
- `IPipelineCommandBehavior<TMessage>`
- `IPipelineRequestBehavior<TMessage, TResponse>`
- `IPipelineNotificationBehavior<TMessage>`
- `IPipelineStreamBehavior<TMessage, TResponse>`
- `PipelineBehaviorDelegate<TMessage, TResult>`
- `HandlerExecutionDelegate<THandler, TMessage, TResult>`
