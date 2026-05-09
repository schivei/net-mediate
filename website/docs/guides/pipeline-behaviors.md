---
sidebar_position: 5
---

# Pipeline Behaviors

> **GenDI pattern:** Use `[Injectable]` + `[Inject]` for regular application services. Concrete non-generic pipeline behaviors can also use `[Injectable]`. Reserve manual `builder.Services` registration for generic/open behavior implementations.

Pipeline behaviors are middleware-style interceptors that wrap handler execution.

## Example

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record MyRequest(string Id);
public record MyResponse(string Id);

[Injectable(ServiceLifetime.Singleton, Group = 10, Order = 1)]
public sealed class LoggingBehavior : IPipelineRequestBehavior<MyRequest, MyResponse>
{
    public async Task<MyResponse> Handle(
        object? key,
        MyRequest message,
        PipelineBehaviorDelegate<MyRequest, Task<MyResponse>> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Before: {nameof(MyRequest)} (key={key})");
        var response = await next(key, message, cancellationToken);
        Console.WriteLine($"After: {nameof(MyRequest)}");
        return response;
    }
}

builder.Services.AddNetMediate();
```
