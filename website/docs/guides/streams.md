---
sidebar_position: 4
---

# Streams

Streams handle requests that return multiple values over time using `IAsyncEnumerable<T>`.

For detailed stream documentation, see the main [README](https://github.com/schivei/net-mediate#streams).

## Usage

```csharp
await foreach (var item in mediator.RequestStream<GetEventsQuery, EventDto>(new GetEventsQuery()))
{
    Console.WriteLine(item);
}
```
