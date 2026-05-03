# AOT / Trimming Support

NetMediate is designed to be fully compatible with NativeAOT-compiled and trimmed applications.

## Summary

NetMediate does **not** perform assembly scanning. All handler types must be registered explicitly, either manually via `IMediatorServiceBuilder` or automatically via the source generator.

| Path | AOT / Trim compatible | Notes |
|---|---|---|
| Explicit registration (`AddNetMediate(configure)`) | ✅ Yes | All handler types registered explicitly |
| Source generation (`AddNetMediateGenerated()`) | ✅ Yes | Generated at compile time — no reflection |
| Open-generic behaviors (`IPipelineBehavior<,>`, `IPipelineRequestBehavior<,>`) | ⚠️ Partial | Open-generic registration relies on the DI container's own reflection; use `RegisterBehavior<>` on the builder for full AOT safety |

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
method that registers them explicitly using `RegisterHandler<>` calls — fully AOT-safe.

### Option 2: Manual explicit registration

```csharp
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterCommandHandler<CreateUserCommandHandler, CreateUserCommand>();
    configure.RegisterNotificationHandler<UserCreatedHandler, UserCreated>();
    configure.RegisterRequestHandler<GetUserQueryHandler, GetUserQuery, UserDto>();
    configure.RegisterStreamHandler<GetEventsQueryHandler, GetEventsQuery, EventDto>();
});
```

### Option 3: Register behaviors explicitly (fully AOT-safe)

```csharp
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterBehavior<AuditBehavior<MyRequest, Task<MyResponse>>, MyRequest, Task<MyResponse>>();
    // ...
});
```

## AOT-unsafe patterns to avoid

- Calling `MakeGenericType` at runtime — not supported by NativeAOT
- Using `Type.GetGenericArguments()` to construct service types at runtime
- Registering open-generic behaviors via `typeof(T)` if those types use `MakeGenericType` internally
