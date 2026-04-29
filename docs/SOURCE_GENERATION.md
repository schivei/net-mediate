# NetMediate.SourceGeneration

`NetMediate.SourceGeneration` is an optional Roslyn analyzer package that generates handler
registrations at compile time.  This serves two purposes:

1. **Eliminates startup reflection cost** — assembly scanning is replaced with a generated
   explicit registration call.
2. **Native AOT / trimmer safety** — the generated registration uses only concrete type
   references, making it fully compatible with `PublishAot` and aggressive linker trimming.

## Installation

```xml
<!-- Use OutputItemType="Analyzer" + ReferenceOutputAssembly="false" for source generators -->
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Usage

```csharp
using NetMediate;

var builder = Host.CreateApplicationBuilder();

// Replace AddNetMediate() + assembly scan with the generated registration
builder.Services.AddNetMediateGenerated();

var host = builder.Build();
await host.RunAsync();
```

### What is generated?

The source generator scans your project at compile time for all classes that implement:

- `ICommandHandler<TMessage>`
- `IRequestHandler<TMessage, TResponse>`
- `INotificationHandler<TMessage>`
- `IStreamHandler<TMessage, TResponse>`
- `IValidationHandler<TMessage>`
- `ICommandBehavior<TMessage>`
- `IRequestBehavior<TMessage, TResponse>`
- `INotificationBehavior<TMessage>`
- `IStreamBehavior<TMessage, TResponse>`

It emits an `AddNetMediateGenerated(IServiceCollection services)` extension method with
explicit `services.AddSingleton<ICommandHandler<MyCmd>, MyCmdHandler>()` calls — no
reflection, no assembly scanning.

### Excluding generated code from coverage

```csharp
builder.Services.AddNetMediateGenerated(excludeFromCodeCoverage: true);
```

## Manual explicit registration (no source generator)

If you prefer to control registrations explicitly without the source generator:

```csharp
builder.Services.AddNetMediate(registration =>
{
    registration.RegisterCommandHandler<CreateUserCommand, CreateUserCommandHandler>();
    registration.RegisterRequestHandler<GetUserRequest, UserDto, GetUserRequestHandler>();
    registration.RegisterNotificationHandler<UserCreated, SendWelcomeEmailHandler>();
    registration.RegisterNotificationHandler<UserCreated, AuditLogHandler>();
});
```

## Disabling validation and telemetry at compile time

The source generator respects `DisableTelemetry()` and `DisableValidation()` on the builder.
When disabled, the generated code does not emit the corresponding pipeline hooks, reducing
the per-dispatch call overhead to near-zero:

```csharp
builder.Services.AddNetMediateGenerated()
    .DisableTelemetry()
    .DisableValidation();
```

## Interaction with notification providers

Source generation is orthogonal to notification dispatch strategy.  You can use
`AddNetMediateGenerated()` with any `INotificationProvider` registration:

```csharp
builder.Services.AddNetMediateGenerated();
builder.Services.AddNetMediateInternalNotifier(); // channel-based, optional
```
