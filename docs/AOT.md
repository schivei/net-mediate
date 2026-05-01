# AOT / Trimming Support

This document describes how to use NetMediate in NativeAOT-compiled and trimmed applications.

## Summary

NetMediate supports two handler-registration paths. Only one of them is compatible with NativeAOT and trimming.

| Path | Compatible with NativeAOT / trimming | Notes |
|---|---|---|
| Assembly scanning (`AddNetMediate()`, `AddNetMediate(assemblies)`) | ❌ No | Annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` |
| Explicit registration (`AddNetMediate(configure)`, `AddNetMediateGenerated()`) | ✅ Yes | All handler types are registered explicitly at compile time |
| `MediatorExtensions.Request<TResponse>()` / `RequestStream<TResponse>()` | ❌ No | Use the two-generic-argument overloads instead |

## AOT-compatible setup

### Option 1: Source-generation package (recommended)

Install `NetMediate.SourceGeneration` as an analyzer-only package and call the generated extension method:

```xml
<PackageReference Include="NetMediate.SourceGeneration"
                  Version="x.x.x"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

```csharp
// Generated at compile time — no reflection at startup
builder.Services.AddNetMediateGenerated();
```

The source generator discovers handler types in your project and generates an `AddNetMediateGenerated()` extension
method that registers them explicitly, bypassing all reflection-based scanning.

### Option 2: Manual explicit registration via `AddNetMediate(Action<IMediatorServiceBuilder>)`

```csharp
builder.Services.AddNetMediate(mediator =>
{
    // Register each handler type directly — fully AOT-safe
    mediator.Services.AddSingleton<ICommandHandler<CreateUserCommand>, CreateUserCommandHandler>();
    mediator.Services.AddSingleton<INotificationHandler<UserCreated>, UserCreatedHandler>();
    mediator.Services.AddSingleton<IRequestHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
});
```

This overload creates the mediator infrastructure without any assembly scanning.
Every handler type you register inside the callback is added to DI explicitly, which is fully safe for trimming and NativeAOT.

### Prefer explicit generic dispatch overloads

The `MediatorExtensions.Request<TResponse>` and `MediatorExtensions.RequestStream<TResponse>` helper methods
use `MethodInfo.MakeGenericMethod` at runtime and are not compatible with NativeAOT. Use the two-argument
overloads on `IMediator` directly:

```csharp
// ❌ Not AOT-safe
var dto = await mediator.Request(new GetUserQuery("id"), cancellationToken);

// ✅ AOT-safe — both type arguments are explicit
var dto = await mediator.Request<GetUserQuery, UserDto>(new GetUserQuery("id"), cancellationToken);
```

## Trimming in publish profiles

When publishing with `dotnet publish --self-contained -r <rid>`, add trim options to your project file:

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimmerRootAssembly Include="YourApp" />
</PropertyGroup>
```

When the assembly-scanning overloads are called from code with trimming enabled, the .NET trimmer emits
`IL2026`/`IL3050` warnings because the overloads are marked with `[RequiresUnreferencedCode]` /
`[RequiresDynamicCode]`. Switch to the AOT-compatible registration paths above to eliminate these warnings.

## `NetMediate.Quartz` and NativeAOT

`QuartzNotificationJob` uses `MethodInfo.MakeGenericMethod` internally to dispatch notifications by runtime type.
It is marked `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`. If you need NativeAOT for the host running
Quartz-backed notifications, file an issue to discuss a code-generated dispatch path.

## `NetMediate.Adapters` and NativeAOT

`NetMediate.Adapters` registers adapter pipeline behaviors through standard DI generic registrations and does not
use reflection-based scanning. It is fully compatible with NativeAOT when adapter implementations and message
types are registered explicitly.
