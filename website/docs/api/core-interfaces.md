---
sidebar_position: 1
---

# Core Interfaces

## IMediator

The main interface for sending messages.

```csharp
public interface IMediator
{
    Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default);
    Task<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default);
    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default);
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
