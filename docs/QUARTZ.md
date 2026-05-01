# NetMediate.Quartz

`NetMediate.Quartz` is an optional additional package alongside `NetMediate.Resilience` that integrates [Quartz.NET](https://www.quartz-scheduler.net/) into the notification pipeline.

## Why Quartz for notifications?

The default NetMediate notification transport is an in-memory `Channel<T>` processed by a background service. This is fast and appropriate for most scenarios. However, if your process crashes before a notification is dispatched, the in-memory queue is lost.

`NetMediate.Quartz` replaces the in-memory transport with persistent Quartz jobs:

- **Crash recovery** — if the process terminates before a job fires, Quartz (with a persistent `AdoJobStore`) replays the job on the next startup.
- **Cluster distribution** — with Quartz clustering enabled, notification jobs are load-balanced across nodes.

> This integration affects **only notifications**. Commands, requests, and streams continue to use the core in-process pipeline.

## Installation

```bash
dotnet add package NetMediate.Quartz
```

You will also need Quartz itself and its hosting package:

```bash
dotnet add package Quartz
dotnet add package Quartz.Extensions.DependencyInjection
dotnet add package Quartz.Extensions.Hosting
```

## Quick start

```csharp
using NetMediate.Quartz;
using Quartz;

var builder = Host.CreateApplicationBuilder();

// 1. Configure Quartz (use AdoJobStore for persistence)
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    // For persistence, configure q.UseJobStore<...>() here.
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// 2. Register NetMediate with Quartz as the notification transport
builder.Services.AddNetMediateQuartz(opts =>
{
    opts.GroupName = "MyApp";
});

var host = builder.Build();
await host.RunAsync();
```

## Configuration

`AddNetMediateQuartz` accepts an optional `QuartzNotificationOptions` callback:

| Property | Default | Description |
|---|---|---|
| `GroupName` | `"NetMediate"` | Quartz group name for all notification jobs. |
| `MisfireRetryCount` | `1` | How many times Quartz will retry a misfired job. |

```csharp
builder.Services.AddNetMediateQuartz(opts =>
{
    opts.GroupName = "Notifications";
    opts.MisfireRetryCount = 3;
});
```

## Customizing serialization

By default messages are serialized with `System.Text.Json`. You can replace the serializer by registering a custom `INotificationSerializer` *after* `AddNetMediateQuartz`:

```csharp
builder.Services.AddNetMediateQuartz();

// Replace with a custom serializer (e.g., MessagePack)
builder.Services.AddSingleton<INotificationSerializer, MyMessagePackSerializer>();
```

## Persistent job store (recommended for production)

For crash recovery, configure Quartz with a persistent store before calling `AddNetMediateQuartz`:

```csharp
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    q.UsePersistentStore(store =>
    {
        store.UseProperties = true;
        store.UseSqlServer("Server=...;Database=Quartz;...");
        store.UseJsonSerializer();
    });
});
```

## Cluster mode

Enable Quartz clustering to distribute notification execution across nodes:

```csharp
builder.Services.AddQuartz(q =>
{
    q.SchedulerName = "NetMediateCluster";
    q.SchedulerId = "AUTO";

    q.UsePersistentStore(store =>
    {
        store.UseProperties = true;
        store.UseSqlServer("...");
        store.UseJsonSerializer();
        store.UseClustering();
    });
});
```

## Architecture notes

```
IMediator.Notify(message)
    └─► INotifiable.Notify (QuartzNotifier)
            └─► Quartz schedules QuartzNotificationJob
                    └─► (job fires) QuartzNotificationJob.Execute
                            └─► INotifiable.DispatchNotifications (QuartzNotifier)
                                    └─► Validation + Behaviors + Handlers
```

`QuartzNotificationJob` stores two values in the Quartz `JobDataMap`:

| Key | Value |
|---|---|
| `netmediate_message` | JSON-serialized notification payload |
| `netmediate_type` | Assembly-qualified CLR type name |

The generic dispatch is cached per message type using a `ConcurrentDictionary` to minimise reflection overhead after the first invocation.

## Target frameworks

`NetMediate.Quartz` is published for:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`
