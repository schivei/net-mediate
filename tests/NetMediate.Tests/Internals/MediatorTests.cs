using Microsoft.Extensions.DependencyInjection;
using Moq;
using NetMediate.Internals;
using System.ComponentModel.DataAnnotations;

using Notifier = NetMediate.Moq.Notifier;

namespace NetMediate.Tests.Internals;

public class MediatorTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mediator _mediator;
    private readonly Mock<INotifiable> _notifier;

    public MediatorTests()
    {
        _serviceProviderMock = new();
        _notifier = new();

        _mediator = new Mediator(
            _serviceProviderMock.Object,
            _notifier.Object
        );
    }

    #region Notify Tests

    [Fact]
    public async Task Notify_WithValidMessage_ShouldDelegateToNotifier()
    {
        // Arrange
        var message = new TestMessageNotification { Content = "Test" };
        _notifier.Setup(n => n.Notify(message, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        await _mediator.Notify(message, TestContext.Current.CancellationToken);

        // Assert
        _notifier.Verify(n => n.Notify(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notify_Enumerable_ShouldDelegateToNotifier()
    {
        TestMessageNotification[] messages = [new() { Content = "1" }, new() { Content = "2" }];
        _notifier.Setup(n => n.Notify(messages, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _mediator.Notify(messages, TestContext.Current.CancellationToken);

        _notifier.Verify(n => n.Notify(messages, It.IsAny<CancellationToken>()), Times.Once);
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
            .Returns(Task.CompletedTask);

        SetupHandler(handler.Object);
        SetupPipelineExecutor<TestMessageCommand, Task, ICommandHandler<TestMessageCommand>>();

        // Act
        await _mediator.Send(message, TestContext.Current.CancellationToken);

        // Assert
        handler.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithNoHandler_ShouldComplete()
    {
        // Arrange — no handlers registered; command dispatch silently completes
        var message = new TestMessageCommand { Content = "Test" };
        SetupHandler<ICommandHandler<TestMessageCommand>>([]);
        SetupPipelineExecutor<TestMessageCommand, Task, ICommandHandler<TestMessageCommand>>();

        // Act — should NOT throw; foreach on empty handlers is a no-op
        await _mediator.Send(message, TestContext.Current.CancellationToken);
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
        SetupPipelineExecutor<TestMessageCommand, Task, ICommandHandler<TestMessageCommand>>();

        // Act & Assert — exception propagates through the catch/rethrow in Send
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mediator.Send(message, TestContext.Current.CancellationToken)
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
        SetupPipelineExecutor<TestRequest, Task<TestResponse>, IRequestHandler<TestRequest, TestResponse>>();

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
        SetupPipelineExecutor<TestRequest, Task<TestResponse>, IRequestHandler<TestRequest, TestResponse>>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _mediator.Request<TestRequest, TestResponse>(
                message,
                TestContext.Current.CancellationToken
            )
        );
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
        SetupPipelineExecutor<TestRequest, Task<TestResponse>, IRequestHandler<TestRequest, TestResponse>>();

        // Act & Assert — exception propagates through the catch/rethrow in Request
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mediator.Request<TestRequest, TestResponse>(message, TestContext.Current.CancellationToken)
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
        SetupPipelineExecutor<TestStream, IAsyncEnumerable<TestResponse>, IStreamHandler<TestStream, TestResponse>>();

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
        SetupPipelineExecutor<TestStream, IAsyncEnumerable<TestResponse>, IStreamHandler<TestStream, TestResponse>>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (
                var _ in _mediator.RequestStream<TestStream, TestResponse>(
                    message,
                    TestContext.Current.CancellationToken
                )
            ) { }
        });
    }

    #endregion

    #region Notifies Tests

    [Fact]
    public async Task Notify_WhenNotifierThrows_PropagatesException()
    {
        // Arrange
        var message = new TestMessageNotification { Content = "Test" };
        _notifier.Setup(n => n.Notify(message, It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("dispatch error"));

        // Act & Assert — exception from notifier propagates through Mediator.Notify
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mediator.Notify(message, TestContext.Current.CancellationToken)
        );
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

    private void SetupPipelineExecutor<TMessage, TResult, THandler>()
        where TMessage : notnull
        where TResult : notnull
        where THandler : class, IHandler<TMessage, TResult>
    {
        // Return a real PipelineExecutor wired to the mock service provider
        var executor = new PipelineExecutor<TMessage, TResult, THandler>(_serviceProviderMock.Object);
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(PipelineExecutor<TMessage, TResult, THandler>)))
            .Returns(executor);
        // Return empty behaviors by default
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(IEnumerable<IPipelineBehavior<TMessage, TResult>>)))
            .Returns(Array.Empty<IPipelineBehavior<TMessage, TResult>>());
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
                ShouldFail ? new("Validation failed") : ValidationResult.Success!
            );
    }

    #endregion
}
