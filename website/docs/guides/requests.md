---
sidebar_position: 2
---

# Requests

Requests follow the request-response pattern, returning a typed result from a single handler.

For detailed request documentation, see the main [README](https://github.com/schivei/net-mediate#requests).

## Usage

```csharp
var user = await mediator.Request<GetUserQuery, UserDto>(new GetUserQuery("user-123"));
```
