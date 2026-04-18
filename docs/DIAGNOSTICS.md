# Diagnostics: Structured Logs and Metrics by Message Type

## Structured logs

NetMediate already emits structured message-type logs in core mediator operations, for example:

- `Sending message of type {MessageType}`
- `Notifying message of type {MessageType}`
- `Resolved {HandlerCount} handlers for message of type {MessageType}`

This allows filtering by `MessageType` in log platforms.

## Metrics by message type (application-level instrumentation)

A simple way to capture metrics is wrapping mediator calls and tagging by message type:

```csharp
using System.Diagnostics.Metrics;

public sealed class MediatorDiagnostics
{
    private static readonly Meter Meter = new("NetMediate.App");
    private static readonly Counter<long> SendCounter = Meter.CreateCounter<long>("netmediate.send.count");

    public static async Task SendWithMetrics<TMessage>(IMediator mediator, TMessage message, CancellationToken ct)
    {
        SendCounter.Add(1, KeyValuePair.Create<string, object?>("message_type", typeof(TMessage).Name));
        await mediator.Send(message, ct);
    }
}
```

For MediatR contracts using compat, the same approach applies with `MediatR.IMediator`.

## Validation checklist

- [x] Structured log templates include message type
- [x] Message-type metric tags documented for app-level counters
- [x] Shared parity tests validate message dispatch in both package modes
