---
sidebar_position: 5
---

# Pipeline Behaviors

> **GenDI pattern:** Use `[Injectable]` + `[Inject]` for regular application services. For pipeline behaviors and other generic-service contracts, register them manually in `builder.Services`.

Pipeline behaviors are middleware-style interceptors that wrap handler execution.

## Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record MyRequest(string Id);
public record MyResponse(string Id);

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
builder.Services.AddSingleton<IPipelineRequestBehavior<MyRequest, MyResponse>, LoggingBehavior>();
```
