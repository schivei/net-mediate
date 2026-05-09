---
sidebar_position: 5
---

# Pipeline Behaviors

> **GenDI pattern:** Behaviors and the services they depend on can follow the GenDI `[Injectable]` + `[Inject]` style. Register them as **closed types** so they are resolved directly from DI when `AddNetMediate()` runs.

Pipeline behaviors are middleware-style interceptors that wrap handler execution.

## Example

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record MyRequest(string Id);
public record MyResponse(string Id);

[Injectable<IPipelineRequestBehavior<MyRequest, MyResponse>>(ServiceLifetime.Singleton, Group = 10, Order = 1)]
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
