# AOT / Trimming Support

NetMediate is fully compatible with NativeAOT-compiled and trimmed applications.

## Summary

Handler registration is generated at compile time by `NetMediate.SourceGeneration` — there is no assembly scanning and no reflection involved in registering handlers.

| Path | AOT / Trim compatible | Notes |
|---|---|---|
| Source generation (`AddNetMediateGenerated()`) | ✅ Yes | Generated at compile time — no reflection |
| Open-generic behaviors (`IPipelineBehavior<,>`, `IPipelineRequestBehavior<,>`) | ⚠️ Partial | Open-generic registration relies on the DI container's own reflection; use `RegisterBehavior<>` on the builder for full AOT safety |

## AOT-compatible setup

### Step 1: Install `NetMediate.SourceGeneration`

```xml
<PackageReference Include="NetMediate.SourceGeneration"
                  Version="x.x.x"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### Step 2: Call the generated extension method

```csharp
// Generated at compile time — no reflection at startup
builder.Services.AddNetMediateGenerated();
```

The source generator discovers all handler types in your project and emits closed-type `Register*Handler<>` calls — fully AOT-safe.

### Registering behaviors explicitly (fully AOT-safe)

Pipeline behaviors can also be registered via the builder to avoid any open-generic reflection:

```csharp
// Closed-type, fully AOT-safe — register via AddNetMediate only for behaviors
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterBehavior<AuditBehavior<MyRequest, Task<MyResponse>>, MyRequest, Task<MyResponse>>();
});
```

## AOT-unsafe patterns to avoid

- Calling `MakeGenericType` at runtime — not supported by NativeAOT
- Using `Type.GetGenericArguments()` to construct service types at runtime
- Registering open-generic behaviors via `typeof(T)` if those types use `MakeGenericType` internally
