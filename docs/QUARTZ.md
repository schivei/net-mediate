# NetMediate.Quartz

> **GenDI pattern:** The examples below assume `NetMediate.SourceGeneration` in the startup project. Prefer `[Injectable]` + `[Inject]` for serializers, notifiers, and supporting services.

`NetMediate.Quartz` is an optional package that swaps the default NetMediate notification transport for Quartz-backed persistence.

## Why Quartz for notifications?

The default NetMediate notification path dispatches handlers immediately in-process. `NetMediate.Quartz` persists notifications as Quartz jobs so they can survive process restarts and run across clustered nodes.

> This integration affects **only notifications**. Commands, requests, and streams continue to use the normal NetMediate pipeline.

## Installation

```bash
dotnet add package NetMediate.Quartz
dotnet add package Quartz
dotnet add package Quartz.Extensions.DependencyInjection
dotnet add package Quartz.Extensions.Hosting
```

## Quick start

```csharp
using NetMediate.Quartz;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Quartz first
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    // For persistence, configure q.UseJobStore<...>() here.
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// 2. Register the generated NetMediate services
builder.Services.AddNetMediate();

// 3. Replace the default notifier with the Quartz notifier
builder.Services.AddNetMediateQuartz(opts =>
{
    opts.GroupName = "MyApp";
});

var host = builder.Build();
await host.RunAsync();
```

`AddNetMediateQuartz()` does **not** call `AddNetMediate()` for you. It only wires the Quartz-based `INotifiable` implementation and related options.

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

By default messages are serialized with `System.Text.Json`. You can replace the serializer after `AddNetMediateQuartz`:

```csharp
builder.Services.AddNetMediate();
builder.Services.AddNetMediateQuartz();

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
