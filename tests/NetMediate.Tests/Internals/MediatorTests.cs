using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;
using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Notifier = NetMediate.Moq.Notifier;

namespace NetMediate.Tests.Internals;

public class MediatorTests
{
    private readonly Mock<ILogger<Mediator>> _loggerMock;
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private readonly Mock<IServiceScope> _serviceScopeMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Configuration _configuration;
    private readonly Mediator _mediator;
    private readonly ITerminator _terminator;
    private readonly Mock<Notifier> _notifier;

    public MediatorTests()
    {
        _loggerMock = new Mock<ILogger<Mediator>>();
        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        _serviceScopeMock = new Mock<IServiceScope>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _terminator = Mock.Of<ITerminator>();

        _configuration = new Configuration(Channel.CreateUnbounded<IPack>())
        {
            IgnoreUnhandledMessages = false
        };

        _serviceScopeFactoryMock.Setup(f => f.CreateScope()).Returns(_serviceScopeMock.Object);
        _serviceScopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
        _serviceScopeMock.Setup(s => s.Dispose());

        _notifier = new Mock<Notifier>(_serviceScopeFactoryMock.Object)
        {
            CallBase = true
        };

        _mediator = new Mediator(
            _configuration,
            _serviceScopeFactoryMock.Object,
            _notifier.Object,
            _loggerMock.Object
        );
    }

    #region Notify Tests

    [Fact]
    public async Task Notify_WithValidMessage_ShouldWriteToChannel()
    {
        // Arrange
        var message = new TestMessageNotification { Content = "Test" };
        var handler = new Mock<INotificationHandler<TestMessageNotification>>();
        handler.Setup(h => h.Handle(message, It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        SetupHandler(handler.Object);

        // Act
        await _mediator.Notify(
            message,
            TestContext.Current.CancellationToken
        );

        // Assert
        _notifier.Verify(n => n.DispatchNotifications(message, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Notify_Enumerable_ShouldWriteAllToChannel()
    {
        TestMessageNotification[] messages = [new TestMessageNotification { Content = "1" }, new TestMessageNotification { Content = "2" }];
        var handler = new Mock<INotificationHandler<TestMessageNotification>>();
        handler.Setup(h => h.Handle(It.IsAny<TestMessageNotification>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        SetupHandler(handler.Object);

        await _mediator.Notify(
            messages,
            TestContext.Current.CancellationToken
        );

        _notifier.Verify(n => n.DispatchNotifications(messages[0], TestContext.Current.CancellationToken));
        _notifier.Verify(n => n.DispatchNotifications(messages[1], TestContext.Current.CancellationToken));
    }

    #endregion

    #region Send Tests

    [Fact]
    public async Task Send_WithValidMessageAndHandler_ShouldCallHandler()
    {
        // Arrange
        var message = new TestMessageCommand { Content = "Test" };
        var handler = new Mock<ICommandHandler<TestMessageCommand>>();
        handler
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        SetupHandler(handler.Object);

        // Act
        await _mediator.Send(message, TestContext.Current.CancellationToken);

        // Assert
        handler.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var message = new TestMessageCommand { Content = "Test" };
        SetupHandler<ICommandHandler<TestMessageCommand>>([]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _mediator.Send(message, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Send_WithNoHandlerAndIgnoreUnhandledMessages_ShouldNotThrow()
    {
        // Arrange
        var message = new TestMessageCommand { Content = "Test" };
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<ICommandHandler<TestMessageCommand>>([]);

        // Act
        await _mediator.Send(message, TestContext.Current.CancellationToken);

        // Assert — completing without exception is the expected behaviour when ignoring unhandled messages
    }

    [Fact]
    public async Task Send_WhenHandlerThrows_PropagatesException()
    {
        // Arrange
        var message = new TestMessageCommand { Content = "Test" };
        var handler = new Mock<ICommandHandler<TestMessageCommand>>();
        handler.Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("handler error"));
        SetupHandler(handler.Object);

        // Act & Assert — exception propagates through the catch/rethrow in Send
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mediator.Send(message, TestContext.Current.CancellationToken).AsTask()
        );
    }

    [Fact]
    public async Task Send_WithRegisteredValidationHandlerThatFails_ShouldThrowMessageValidationException()
    {
        // Arrange
        var message = new TestMessageCommand { Content = "Test" };
        var handler = new Mock<ICommandHandler<TestMessageCommand>>();
        handler.Setup(h => h.Handle(message, It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        SetupHandler(handler.Object);

        var validationHandler = new Mock<IValidationHandler<TestMessageCommand>>();
        validationHandler.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
                         .ReturnsAsync(new System.ComponentModel.DataAnnotations.ValidationResult("Content is required."));
        SetupHandler(validationHandler.Object);

        // Act & Assert — registered validation handler failure throws MessageValidationException
        await Assert.ThrowsAsync<MessageValidationException>(
            () => _mediator.Send(message, TestContext.Current.CancellationToken).AsTask()
        );
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
        var result = await _mediator.Request<TestRequest, TestResponse>(
            message,
            TestContext.Current.CancellationToken
        );

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
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _mediator.Request<TestRequest, TestResponse>(
                message,
                TestContext.Current.CancellationToken
            )
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
        var result = await _mediator.Request<TestRequest, TestResponse>(
            message,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Request_WhenHandlerThrows_PropagatesException()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        var handler = new Mock<IRequestHandler<TestRequest, TestResponse>>();
        handler.Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("handler error"));
        SetupHandler(handler.Object);

        // Act & Assert — exception propagates through the catch/rethrow in Request
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mediator.Request<TestRequest, TestResponse>(message, TestContext.Current.CancellationToken).AsTask()
        );
    }

    [Fact]
    public async Task Request_WithRegisteredValidationHandlerThatFails_ShouldThrowMessageValidationException()
    {
        // Arrange
        var message = new TestRequest { Id = 1 };
        var handler = new Mock<IRequestHandler<TestRequest, TestResponse>>();
        handler.Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new TestResponse());
        SetupHandler(handler.Object);

        var validationHandler = new Mock<IValidationHandler<TestRequest>>();
        validationHandler.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
                         .ReturnsAsync(new System.ComponentModel.DataAnnotations.ValidationResult("Id must be positive."));
        SetupHandler(validationHandler.Object);

        // Act & Assert — registered validation handler failure throws MessageValidationException
        await Assert.ThrowsAsync<MessageValidationException>(
            () => _mediator.Request<TestRequest, TestResponse>(message, TestContext.Current.CancellationToken).AsTask()
        );
    }

    #endregion

    #region RequestStream Tests

    [Fact]
    public async Task RequestStream_WithValidMessageAndHandler_ShouldReturnStream()
    {
        // Arrange
        var message = new TestStream { Id = 1 };
        var responses = new[]
        {
            new TestResponse { Value = "Response1" },
            new TestResponse { Value = "Response2" },
        };

        var handler = new Mock<IStreamHandler<TestStream, TestResponse>>();
        handler
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(responses));

        SetupHandler(handler.Object);

        // Act
        var results = new List<TestResponse>();
        await foreach (
            var item in _mediator.RequestStream<TestStream, TestResponse>(
                message,
                TestContext.Current.CancellationToken
            )
        )
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
        var message = new TestStream { Id = 1 };
        SetupHandler<IStreamHandler<TestStream, TestResponse>>([]);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (
                var _ in _mediator.RequestStream<TestStream, TestResponse>(
                    message,
                    TestContext.Current.CancellationToken
                )
            ) { }
        });
        Assert.Contains("No handler found", ex.Message);
    }

    [Fact]
    public async Task RequestStream_WithNoHandlerAndIgnoreUnhandledMessages_ShouldReturnEmptyStream()
    {
        // Arrange
        var message = new TestStream { Id = 1 };
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<IStreamHandler<TestStream, TestResponse>>([]);

        // Act
        var results = new List<TestResponse>();
        await foreach (
            var item in _mediator.RequestStream<TestStream, TestResponse>(
                message,
                TestContext.Current.CancellationToken
            )
        )
        {
            results.Add(item);
        }

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region Notifies Tests

    [Fact]
    public async Task Notifies_WithValidMessageAndHandlers_ShouldCallAllHandlers()
    {
        // Arrange
        var message = new TestMessageNotification { Content = "Test" };
        var handler1 = new Mock<INotificationHandler<TestMessageNotification>>();
        var handler2 = new Mock<INotificationHandler<TestMessageNotification>>();

        handler1
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        handler2
            .Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        SetupHandler([handler1.Object, handler2.Object]);

        // Act
        await _mediator.Notify(
            message,
            TestContext.Current.CancellationToken
        );

        // Assert
        handler1.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
        handler2.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notifies_WithNoHandlers_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var message = new TestMessageNotification { Content = "Test" };
        SetupHandler<INotificationHandler<TestMessageNotification>>([]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _mediator.Notify(
                message,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Notifies_WithNoHandlersAndIgnoreUnhandledMessages_ShouldNotThrow()
    {
        // Arrange
        var message = new TestMessageNotification { Content = "Test" };
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<INotificationHandler<TestMessageNotification>>([]);

        // Act
        await _mediator.Notify(
            message,
            TestContext.Current.CancellationToken
        );

        // Assert — completing without exception is the expected behaviour when ignoring unhandled messages
    }

    [Fact]
    public async Task Notify_Enumerable_WithNoHandlersAndIgnore_ShouldComplete()
    {
        // Arrange
        TestMessageNotification[] messages = [new() { Content = "A" }, new() { Content = "B" }];
        _configuration.IgnoreUnhandledMessages = true;
        SetupHandler<INotificationHandler<TestMessageNotification>>([]);

        // Act — NotifyCore(IList) path with no handlers + ignore
        await _mediator.Notify(messages, TestContext.Current.CancellationToken);

        // Assert — no exception
    }

    [Fact]
    public async Task Notify_Enumerable_WhenNotifierThrows_PropagatesException()
    {
        // Arrange
        var handler = new Mock<INotificationHandler<TestMessageNotification>>();
        handler.Setup(h => h.Handle(It.IsAny<TestMessageNotification>(), It.IsAny<CancellationToken>()))
               .Returns(ValueTask.CompletedTask);
        SetupHandler(handler.Object);

        // DispatchNotifications is virtual — set it up to throw so the enumerable notifier also throws
        _notifier.Setup(n => n.DispatchNotifications(It.IsAny<TestMessageNotification>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("dispatch error"));

        TestMessageNotification[] messages = [new() { Content = "A" }];

        // Act & Assert — exception from notifier propagates through Notify(IEnumerable)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mediator.Notify(messages, TestContext.Current.CancellationToken).AsTask()
        );
    }

    [Fact]
    public async Task RequestStream_WithRegisteredValidationHandlerThatFails_ShouldThrowMessageValidationException()
    {
        // Arrange
        var message = new TestStream { Id = 1 };
        var handler = new Mock<IStreamHandler<TestStream, TestResponse>>();
        handler.Setup(h => h.Handle(message, It.IsAny<CancellationToken>()))
               .Returns(GetAsyncEnumerable([new TestResponse { Value = "x" }]));
        SetupHandler(handler.Object);

        var validationHandler = new Mock<IValidationHandler<TestStream>>();
        validationHandler.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
                         .ReturnsAsync(new System.ComponentModel.DataAnnotations.ValidationResult("Id too small."));
        SetupHandler(validationHandler.Object);

        // Act & Assert
        await Assert.ThrowsAsync<MessageValidationException>(async () =>
        {
            await foreach (var _ in _mediator.RequestStream<TestStream, TestResponse>(message, TestContext.Current.CancellationToken)) { }
        });
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

    private static async IAsyncEnumerable<T> GetAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }

    #endregion

    #region Test Types

    public class TestMessage
    {
        public string Content { get; set; } = string.Empty;
    }

    public class TestMessageNotification : TestMessage, INotification;

    public class TestMessageCommand : TestMessage, ICommand;

    public class TestRequest : TestData, IRequest<TestResponse>;

    public class TestStream : TestData, IStream<TestResponse>;

    public class TestData
    {
        public int Id { get; set; }
    }

    public class TestResponse
    {
        public string Value { get; set; } = string.Empty;
    }

    public class NotificationTestMessage : INotification
    {
        public int Id { get; set; }
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
