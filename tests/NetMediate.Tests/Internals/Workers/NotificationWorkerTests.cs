using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;
using NetMediate.Internals.Workers;

namespace NetMediate.Tests.Internals.Workers;

public class NotificationWorkerTests
{
    private readonly Mock<MediatorTest> _mediatorMock;
    private readonly Mock<ILogger<NotificationWorker>> _loggerMock;
    private readonly Channel<object> _channel;
    private readonly Channel<object> _channel2;
    private readonly Configuration _configuration;
    private readonly Configuration _configuration2;
    private readonly NotificationWorker _worker;
    private readonly NotificationWorker _worker2;

    public NotificationWorkerTests()
    {
        _loggerMock = new Mock<ILogger<NotificationWorker>>();
        _channel = Channel.CreateUnbounded<object>();
        _channel2 = Channel.CreateUnbounded<object>();
        _configuration = new Configuration(_channel);
        _configuration2 = new Configuration(_channel2);
        _mediatorMock = new Mock<MediatorTest>() { CallBase = true };
        _worker = new NotificationWorker(_mediatorMock.Object, _configuration, _loggerMock.Object);
        _worker2 = new NotificationWorker(
            _mediatorMock.Object,
            _configuration2,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesMessages_Successfully()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        await _channel.Writer.WriteAsync(message);
        _channel.Writer.Complete();
        var cts = new CancellationTokenSource();

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing
        await _worker.StopAsync(cts.Token);

        // Assert
        _mediatorMock.Verify(m => m.Notifies(message, It.IsAny<CancellationToken>()), Times.Once);
        VerifyDebugLog("Processing message of type TestMessage:", Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsNullMessages()
    {
        // Arrange
        await _channel.Writer.WriteAsync(null!);
        _channel.Writer.Complete();
        var cts = new CancellationTokenSource();

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing
        await _worker.StopAsync(cts.Token);

        // Assert
        _mediatorMock.Verify(
            m => m.Notifies(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ExecuteAsync_HandlesOperationCanceledException()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        _mediatorMock
            .Setup(m => m.Notifies(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var cts = new CancellationTokenSource();

        await _channel.Writer.WriteAsync(message);

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing

        // Assert
        VerifyDebugLog(
            "System operation was canceled, stopping notification worker.",
            Times.Once()
        );
        await _worker.StopAsync(cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesChannelClosedException()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        _mediatorMock
            .Setup(m => m.Notifies(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelClosedException());
        var cts = new CancellationTokenSource();

        await _channel.Writer.WriteAsync(message);

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing

        // Assert
        VerifyDebugLog("Channel was closed, stopping notification worker.", Times.Once());
        await _worker.StopAsync(cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_LogsUnhandledExceptions_WhenConfigured()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        var exception = new InvalidOperationException("Test exception");
        var cts = new CancellationTokenSource();

        _configuration.IgnoreUnhandledMessages = true;
        _configuration.LogUnhandledMessages = true;
        _configuration.UnhandledMessagesLogLevel = LogLevel.Error;

        _mediatorMock
            .Setup(m => m.Notifies(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        await _channel.Writer.WriteAsync(message);
        _channel.Writer.Complete();

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing
        await _worker.StopAsync(cts.Token);

        // Assert
        VerifyLog(LogLevel.Error, "Error processing message of type TestMessage:", Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsUnhandledExceptions_WhenNotConfiguredToIgnore()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        var exception = new InvalidOperationException("Test exception");
        var cts = new CancellationTokenSource();

        _configuration.IgnoreUnhandledMessages = false;

        _mediatorMock
            .Setup(m => m.Notifies(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        await _channel.Writer.WriteAsync(message);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _worker.StartAsync(cts.Token);
            await Task.Delay(100); // Allow time for processing
        });

        await _worker.StopAsync(cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenCancellationRequested()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        await _channel2.Writer.WriteAsync(message);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        // Act
        await _worker2.StartAsync(cts.Token);
        await Task.Delay(500);

        // Assert
        VerifyDebugLog("Notification worker stopped.", Times.Once());
    }

    private void VerifyDebugLog(string messageContains, Times times)
    {
        _loggerMock.Verify(
            x =>
                x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(messageContains)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            times
        );
    }

    private void VerifyLog(LogLevel level, string messageContains, Times times)
    {
        _loggerMock.Verify(
            x =>
                x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(messageContains)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            times
        );
    }

    public class TestMessage
    {
        public int Id { get; set; }
    }

    public class MediatorTest : IMediator, INotifiable
    {
        public virtual Task Notifies(
            object message,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public virtual Task Notify<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public virtual Task<TResponse> Request<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<TResponse>(default!);

        public virtual async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
            TMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();

            yield break;
        }

        public virtual Task Send<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
