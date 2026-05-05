# AOT / Trimming Support

NetMediate is fully compatible with NativeAOT-compiled and trimmed applications.

## Summary

Handler registration is generated at compile time by `NetMediate.SourceGeneration` — there is no assembly scanning and no reflection involved in registering handlers. Pipeline behaviors must be registered via `RegisterBehavior<>` on the builder; open-generic DI patterns are not supported.

| Path | AOT / Trim compatible | Notes |
|---|---|---|
| Source generation (`AddNetMediate()`) | ✅ Yes | Generated at compile time — no reflection |
| `RegisterBehavior<TBehavior, TMessage, TResult>()` | ✅ Yes | Closed-type — no reflection, fully AOT-safe |
| `RegisterCommandHandler<THandler, TMsg>()` (no key) | ✅ Yes | Resolved via `GetServices<T>()` |
| `RegisterCommandHandler<THandler, TMsg>("key")` | ⚠️ No | Uses `IKeyedServiceProvider` — not NativeAOT-compatible |
| `RegisterNotificationHandler<THandler, TMsg>("key")` | ⚠️ No | Uses `IKeyedServiceProvider` — not NativeAOT-compatible |
| `RegisterRequestHandler<THandler, TMsg, TResp>("key")` | ⚠️ No | Uses `IKeyedServiceProvider` — not NativeAOT-compatible |
| `RegisterStreamHandler<THandler, TMsg, TResp>("key")` | ⚠️ No | Uses `IKeyedServiceProvider` — not NativeAOT-compatible |

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
builder.Services.AddNetMediate();
```

The source generator discovers all handler types in your project and emits closed-type `Register*Handler<>` calls — fully AOT-safe.

### Registering behaviors

Register pipeline behaviors via the builder using closed types:

```csharp
builder.Services.UseNetMediate(configure =>
{
    configure.RegisterBehavior<AuditBehavior<MyRequest, Task<MyResponse>>, MyRequest, Task<MyResponse>>();
});
```

## AOT-unsafe patterns to avoid

- Calling `MakeGenericType` at runtime — not supported by NativeAOT
- Using `Type.GetGenericArguments()` to construct service types at runtime
- Registering behaviors via open-generic `services.AddSingleton(typeof(IPipeline...<,>), typeof(...<,>))` — not supported
- Using keyed handler registration (`Register*Handler<T,M>("routingKey")`) — uses `IKeyedServiceProvider` which is not supported in NativeAOT. Use keyed handlers only when NativeAOT is not required.
