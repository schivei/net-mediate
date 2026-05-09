# AOT / Trimming Support

NetMediate is compatible with NativeAOT and trimming when you stay on the source-generated, closed-type registration path.

## Summary

- Use `NetMediate.SourceGeneration` in the startup project.
- Call `builder.Services.AddNetMediate();`.
- Register custom pipeline behaviors as **closed types** directly in DI.
- For generic-service contracts (for example `IPipelineRequestBehavior<TMessage, TResponse>`), register them manually in `builder.Services` instead of using GenDI attributes.
- Avoid keyed dispatch when NativeAOT is required.

| Path | AOT / Trim compatible | Notes |
|---|---|---|
| `AddNetMediate()` | ✅ Yes | Generated at compile time — no reflection |
| Closed-type pipeline behavior registrations | ✅ Yes | Register `IPipelineCommandBehavior<T>`, `IPipelineNotificationBehavior<T>`, or `IPipelineRequestBehavior<TMessage, TResponse>` directly |
| Keyless `Send` / `Notify` / `Request` / `RequestStream` | ✅ Yes | Uses generated closed-type registrations |
| Keyed dispatch (`Send(key, ...)`, `Request(key, ...)`, etc.) | ⚠️ No | Uses `IKeyedServiceProvider`, which is not NativeAOT-compatible |

## AOT-compatible setup

### Step 1: Install `NetMediate.SourceGeneration`

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x.x">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

> **Contracts-only projects:** Use `NetMediate.Core` in shared libraries that only need the contracts.

### Step 2: Call the generated extension method

```csharp
// Generated at compile time — no reflection at startup
builder.Services.AddNetMediate();
```

The source generator discovers all handler types in your project and emits the closed-type registrations for handlers, executors, and generated dispatch extensions.

### Step 3: Register custom behaviors as closed types

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public sealed class AuditCreateUserBehavior : IPipelineRequestBehavior<CreateUserRequest, UserDto>
{
    public Task<UserDto> Handle(
        object? key,
        CreateUserRequest message,
        PipelineBehaviorDelegate<CreateUserRequest, Task<UserDto>> next,
        CancellationToken cancellationToken) =>
        next(key, message, cancellationToken);
}

builder.Services.AddNetMediate();
builder.Services.AddSingleton<IPipelineRequestBehavior<CreateUserRequest, UserDto>, AuditCreateUserBehavior>();
```

## AOT-unsafe patterns to avoid

- Runtime reflection-based registration
- Open-generic pipeline behavior registration guidance
- Keyed dispatch when the application must stay NativeAOT-compatible
