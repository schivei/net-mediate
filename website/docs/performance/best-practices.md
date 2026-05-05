---
sidebar_position: 2
---

# Best Practices

Tips for optimal performance with NetMediate.

## Handler Design

- Keep handlers focused on a single responsibility
- Avoid blocking calls in async handlers
- Use `ConfigureAwait(false)` for library code (not necessary in ASP.NET Core)

## Cancellation Tokens

Always pass cancellation tokens through the entire call chain:

```csharp
public async Task Handle(MyCommand command, CancellationToken cancellationToken)
{
    await _repository.SaveAsync(data, cancellationToken);
    await _httpClient.GetAsync(url, cancellationToken);
}
```

## Behaviors

- Register only necessary behaviors
- Avoid heavy computation in behaviors
- Consider behavior order carefully

## AOT Compilation

Use source generation for optimal performance in AOT scenarios. See [AOT Support](../advanced/aot-support.md).
