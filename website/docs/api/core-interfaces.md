---
sidebar_position: 1
---

# Core Interfaces

## IMediator

The main interface for sending messages.

```csharp
public interface IMediator
{
    // --- Notify overloads ---
    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task Notify<TMessage>(object? key, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task Notify<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
        where TMessage : notnull;

    // --- Send overloads ---
    Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task Send<TMessage>(object? key, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task Send<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task Send<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
        where TMessage : notnull;

    // --- Request overloads ---
    Task<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task<TResponse> Request<TMessage, TResponse>(object? key, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;

    // --- RequestStream overloads ---
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(object? key, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
}
```

## ICommandHandler&lt;TMessage&gt;

Handler for commands.

```csharp
public interface ICommandHandler<in TMessage> : IHandler<TMessage, Task>
{
    Task Handle(TMessage message, CancellationToken cancellationToken = default);
}
```

## IRequestHandler&lt;TMessage, TResponse&gt;

Handler for requests.

```csharp
public interface IRequestHandler<in TMessage, TResponse> : IHandler<TMessage, Task<TResponse>>
{
    Task<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default);
}
```

## INotificationHandler&lt;TMessage&gt;

Handler for notifications.

```csharp
public interface INotificationHandler<in TMessage> : IHandler<TMessage, Task>
{
    Task Handle(TMessage message, CancellationToken cancellationToken = default);
}
```

## IStreamHandler&lt;TMessage, TResponse&gt;

Handler for streams.

```csharp
public interface IStreamHandler<in TMessage, out TResponse> : IHandler<TMessage, IAsyncEnumerable<TResponse>>
{
    IAsyncEnumerable<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default);
}
```
