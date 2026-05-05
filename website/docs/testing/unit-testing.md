---
sidebar_position: 2
---

# Unit Testing

Learn how to effectively test your NetMediate handlers and behaviors.

## Testing Handlers

Handlers can be tested in isolation by instantiating them directly:

```csharp
[Fact]
public async Task Handle_ShouldCreateUser()
{
    // Arrange
    var repository = new InMemoryUserRepository();
    var handler = new CreateUserHandler(repository);
    var command = new CreateUserCommand("test@example.com");

    // Act
    await handler.Handle(command, CancellationToken.None);

    // Assert
    var users = await repository.GetAllAsync();
    Assert.Single(users);
}
```

## Mocking the Mediator

Use NetMediate.Moq for testing:

```csharp
var services = new ServiceCollection();
var mediatorMock = services.AddMediatorMock();

mediatorMock.Setup(m => m.Request<GetUserQuery, UserDto>(It.IsAny<GetUserQuery>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new UserDto("123", "test@example.com"));
```

For more examples, see [Moq Recipes](./moq-recipes.md).
