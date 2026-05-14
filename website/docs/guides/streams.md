---
sidebar_position: 4
---

# Streams

Streams handle requests that return multiple values over time using `IAsyncEnumerable<T>`.

For detailed stream documentation, see the main [README](https://github.com/schivei/net-mediate#streams).

## Complete Example

The following example returns a real-time audit-log feed for an entity. Two stream handlers are registered: one reads from the current store and another from an archive store. Their items are merged sequentially (all current-store items first, then archive-store items).

```csharp
using System.Runtime.CompilerServices;
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

// ----- Messages -----
public record GetAuditLogQuery(string EntityId, DateTimeOffset Since);
public record AuditLogEntry(DateTimeOffset Timestamp, string Action, string Actor);

// ----- Shared service -----
[ServiceInjection]
public interface IAuditLogStore
{
    IAsyncEnumerable<AuditLogEntry> StreamAsync(
        string entityId,
        DateTimeOffset since,
        CancellationToken ct);
}

// ----- Handler 1: current store (Order = 1, runs first) -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class CurrentAuditLogHandler : IStreamHandler<GetAuditLogQuery, AuditLogEntry>
{
    [Inject] public required IAuditLogStore CurrentStore { get; init; }

    public IAsyncEnumerable<AuditLogEntry> Handle(
        GetAuditLogQuery query, CancellationToken ct) =>
        CurrentStore.StreamAsync(query.EntityId, query.Since, ct);
}

// ----- Handler 2: archive store (Order = 2, runs after handler 1) -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2)]
public class ArchivedAuditLogHandler : IStreamHandler<GetAuditLogQuery, AuditLogEntry>
{
    [Inject(Key = "archive")] public required IAuditLogStore ArchiveStore { get; init; }

    public IAsyncEnumerable<AuditLogEntry> Handle(
        GetAuditLogQuery query, CancellationToken ct) =>
        ArchiveStore.StreamAsync(query.EntityId, query.Since, ct);
}

// ----- Usage: Server-Sent Events endpoint -----
app.MapGet("/entities/{id}/audit", async (
    string id,
    IMediator mediator,
    HttpResponse response,
    CancellationToken ct) =>
{
    response.Headers.ContentType = "text/event-stream";

    var query = new GetAuditLogQuery(id, DateTimeOffset.UtcNow.AddDays(-30));

    await foreach (var entry in mediator.StreamGetAuditLogQueryAsync(query, ct))
    {
        await response.WriteAsync(
            $"data: {entry.Action} by {entry.Actor} at {entry.Timestamp:O}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
});
```

Items from `CurrentAuditLogHandler` are fully yielded before `ArchivedAuditLogHandler` begins. The consumer processes entries one by one with natural backpressure — no buffering required.


