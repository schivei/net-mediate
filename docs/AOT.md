# AOT / Trimming Support

NetMediate is compatible with NativeAOT and trimming when you stay on the source-generated, closed-type registration path.

## Summary

- Use `NetMediate.SourceGeneration` in the startup project.
- Call `builder.Services.AddNetMediate();`.
- Register custom pipeline behaviors as **closed types** directly in DI.
- Concrete non-generic classes that implement **closed generic** contracts can still use `[Injectable]`.
- Register only generic/open service implementations manually in `builder.Services`.
- Keyed dispatch uses GenDI keyed-service registrations and NetMediate keyed dispatch APIs.

| Path | AOT / Trim compatible | Notes |
|---|---|---|
| `AddNetMediate()` | ✅ Yes | Generated at compile time — no reflection |
| Closed-type pipeline behavior registrations | ✅ Yes | Register `IPipelineCommandBehavior<T>`, `IPipelineNotificationBehavior<T>`, or `IPipelineRequestBehavior<TMessage, TResponse>` directly |
| Keyless `Send` / `Notify` / `Request` / `RequestStream` | ✅ Yes | Uses generated closed-type registrations |
| Keyed dispatch (`Send(key, ...)`, `Request(key, ...)`, etc.) | ✅ Yes | GenDI keyed-service resolution (no reflection in NetMediate runtime path), fully NativeAOT + Trimming compatible |

## AOT-compatible setup

### Step 1: Install `NetMediate.SourceGeneration`

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x.x">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>contentfiles; compile; runtime</PrivateAssets>
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
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

[Injectable(ServiceLifetime.Singleton, Group = 10, Order = 1)]
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
```

## AOT-unsafe patterns to avoid

- Runtime reflection-based registration
- Open-generic pipeline behavior registration guidance
