---
sidebar_position: 5
---

# Pipeline Behaviors

Pipeline behaviors are middleware-style interceptors that wrap handler execution, enabling cross-cutting concerns like logging, validation, caching, and more.

For detailed behavior documentation, see the main [README](https://github.com/schivei/net-mediate#pipeline-behaviors--interceptors).

## Example

```csharp
public sealed class LoggingBehavior<TMessage, TResponse>
    : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    public async Task<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Before: {typeof(TMessage).Name}");
        var response = await next(message, cancellationToken);
        Console.WriteLine($"After: {typeof(TMessage).Name}");
        return response;
    }
}
```

## Registration

```csharp
builder.Services.UseNetMediate(configure =>
{
    configure.RegisterBehavior<LoggingBehavior<MyRequest, MyResponse>, MyRequest, Task<MyResponse>>();
});
```
