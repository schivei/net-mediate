using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public class MediatorTests
{
    private readonly Mock<ILogger<Mediator>> _loggerMock;
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private readonly Mock<IServiceScope> _serviceScopeMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Channel<object> _channel;
    private readonly Configuration _configuration;
    private readonly Mediator _mediator;

    public MediatorTests()
    {
        _loggerMock = new Mock<ILogger<Mediator>>();
        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        _serviceScopeMock = new Mock<IServiceScope>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        _channel = Channel.CreateUnbounded<object>();
        _configuration = new Configuration(_channel)
        {
            IgnoreUnhandledMessages = false,
            LogUnhandledMessages = true,
            UnhandledMessagesLogLevel = LogLevel.Warning,
        };

        _serviceScopeFactoryMock.Setup(f => f.CreateScope()).Returns(_serviceScopeMock.Object);
        _serviceScopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
        _serviceScopeMock.Setup(s => s.Dispose());

        _mediator = new Mediator(
            _loggerMock.Object,
            _configuration,
            _serviceScopeFactoryMock.Object
        );
    }

    #region Notify Tests

    [Fact]
    public async Task Notify_WithValidMessage_ShouldWriteToChannel()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };

        // Act
        await _mediator.Notify(message);

        // Assert
        Assert.True(_channel.Reader.TryRead(out var receivedMessage));
        Assert.Same(message, receivedMessage);
    }

    [Fact]
    public async Task Notify_WithNullMessage_ShouldThrowArgumentNullException()
    {
        // Arrange
        TestMessage? message = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _mediator.Notify(message!));
    }

    [Fact]
    public async Task Notify_WithNullMessageAndIgnoreUnhandledMessages_ShouldNotThrow()
    {
        // Arrange
        TestMessage? message = null;
        _configuration.IgnoreUnhandledMessages = true;

        // Act & Assert
        await _mediator.Notify(message!); // Should not throw

        // Verify logging
        VerifyLoggerCalled(LogLevel.Warning, "Received null message");
    }

    [Fact]
    public async Task Notify_WithValidatableMessageThatFails_ShouldThrowValidationException()
    {
        // Arrange
        var message = new TestValidatableMessage { ShouldFail = true };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MessageValidationException>(() =>
            _mediator.Notify(message)
        );
        Assert.Equal("Validation failed", exception.Message);
    }

    [Fact]
    public async Task Notify_WithExternalValidationThatFails_ShouldThrowValidationException()
    {
        // Arrange
        var message = new TestMessage { Content = "Invalid" };
        var validationHandler = new Mock<IValidationHandler<TestMessage>>();
        validationHandler
            .Setup(h => h.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("External validation failed"));

        SetupHandler(validationHandler.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MessageValidationException>(() =>
            _mediator.Notify(message)
        );
        Assert.Equal("External validation failed", exception.Message);
    }

    #endregion

    #region Send Tests

    [Fact]
    public async Task Send_WithValidMessageAndHandler_ShouldCallHandler()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        var handler = new Mock<ICommandHandler<TestMessage>>();
        handler
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetupHandler(handler.Object);

        // Act
        await _mediator.Send(message);

        // Assert
        handler.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        SetupHandler<ICommandHandler<TestMessage>>([]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _mediator.Send(message));
    }

    [Fact]
    public async Task Send_WithNoHandlerAndIgnoreUnhandledMessages_ShouldNotThrow()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<ICommandHandler<TestMessage>>([]);

        // Act
        await _mediator.Send(message);

        // Assert
        VerifyLoggerCalled(LogLevel.Warning, "No handler found");
    }

    #endregion

    #region Request Tests

    [Fact]
    public async Task Request_WithValidMessageAndHandler_ShouldReturnResponse()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        var response = new TestResponse { Value = "Response" };
        var handler = new Mock<IRequestHandler<TestRequest, TestResponse>>();
        handler.Setup(h => h.Handle(message, It.IsAny<CancellationToken>())).ReturnsAsync(response);

        SetupHandler(handler.Object);

        // Act
        var result = await _mediator.Request<TestRequest, TestResponse>(message);

        // Assert
        Assert.Same(response, result);
        handler.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Request_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        SetupHandler<IRequestHandler<TestRequest, TestResponse>>([]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _mediator.Request<TestRequest, TestResponse>(message)
        );
    }

    [Fact]
    public async Task Request_WithNoHandlerAndIgnoreUnhandledMessages_ShouldReturnDefault()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<IRequestHandler<TestRequest, TestResponse>>([]);

        // Act
        var result = await _mediator.Request<TestRequest, TestResponse>(message);

        // Assert
        Assert.Null(result);
        VerifyLoggerCalled(LogLevel.Warning, "No handler found");
    }

    #endregion

    #region RequestStream Tests

    [Fact]
    public async Task RequestStream_WithValidMessageAndHandler_ShouldReturnStream()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        var responses = new[]
        {
            new TestResponse { Value = "Response1" },
            new TestResponse { Value = "Response2" },
        };

        var handler = new Mock<IStreamHandler<TestRequest, TestResponse>>();
        handler
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(responses));

        SetupHandler(handler.Object);

        // Act
        var results = new List<TestResponse>();
        await foreach (var item in _mediator.RequestStream<TestRequest, TestResponse>(message))
        {
            results.Add(item);
        }

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("Response1", results[0].Value);
        Assert.Equal("Response2", results[1].Value);
    }

    [Fact]
    public async Task RequestStream_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        SetupHandler<IStreamHandler<TestRequest, TestResponse>>([]);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _mediator.RequestStream<TestRequest, TestResponse>(message)) { }
        });
        Assert.Contains("No handler found", ex.Message);
    }

    [Fact]
    public async Task RequestStream_WithNoHandlerAndIgnoreUnhandledMessages_ShouldReturnEmptyStream()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<IStreamHandler<TestRequest, TestResponse>>([]);

        // Act
        var results = new List<TestResponse>();
        await foreach (var item in _mediator.RequestStream<TestRequest, TestResponse>(message))
        {
            results.Add(item);
        }

        // Assert
        Assert.Empty(results);
        VerifyLoggerCalled(LogLevel.Warning, "No handler found");
    }

    #endregion

    #region Notifies Tests

    [Fact]
    public async Task Notifies_WithValidMessageAndHandlers_ShouldCallAllHandlers()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        var handler1 = new Mock<INotificationHandler<TestMessage>>();
        var handler2 = new Mock<INotificationHandler<TestMessage>>();

        handler1
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        handler2
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetupHandler<INotificationHandler<TestMessage>>([handler1.Object, handler2.Object]);

        // Act
        await _mediator.Notifies(message);

        // Assert
        handler1.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
        handler2.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notifies_WithNoHandlers_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        SetupHandler<INotificationHandler<TestMessage>>([]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _mediator.Notifies(message));
    }

    [Fact]
    public async Task Notifies_WithNoHandlersAndIgnoreUnhandledMessages_ShouldNotThrow()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<INotificationHandler<TestMessage>>([]);

        // Act
        await _mediator.Notifies(message);

        // Assert
        VerifyLoggerCalled(LogLevel.Warning, "No handler found");
    }

    #endregion

    #region Helpers

    private void SetupHandler<T>(T handler)
        where T : class
    {
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(IEnumerable<T>)))
            .Returns(new[] { handler });
    }

    private void SetupHandler<T>(IEnumerable<T> handlers)
        where T : class
    {
        _serviceProviderMock.Setup(p => p.GetService(typeof(IEnumerable<T>))).Returns(handlers);
    }

    private void VerifyLoggerCalled(LogLevel level, string contains)
    {
        _loggerMock.Verify(
            x =>
                x.Log(
                    It.Is<LogLevel>(l => l == level),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(contains)),
                    It.IsAny<Exception?>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)
                ),
            Times.AtLeastOnce
        );
    }

    private static async IAsyncEnumerable<T> GetAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Delay(1);
        }
    }

    #endregion

    #region Test Types

    public class TestMessage
    {
        public string Content { get; set; } = string.Empty;
    }

    public class TestRequest
    {
        public int Id { get; set; }
    }

    public class TestResponse
    {
        public string Value { get; set; } = string.Empty;
    }

    public class TestValidatableMessage : IValidatable
    {
        public bool ShouldFail { get; set; }

        public Task<ValidationResult> ValidateAsync() =>
            Task.FromResult(
                ShouldFail ? new ValidationResult("Validation failed") : ValidationResult.Success!
            );
    }

    #endregion
}
