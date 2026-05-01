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
    private readonly ITerminator _terminator;
    private readonly Mock<ILogger<NotificationWorker>> _loggerMock;
    private readonly Channel<IPack> _channel;
    private readonly Channel<IPack> _channel2;
    private readonly Configuration _configuration;
    private readonly Configuration _configuration2;
    private readonly NotificationWorker _worker;
    private readonly NotificationWorker _worker2;

    public NotificationWorkerTests()
    {
        _loggerMock = new Mock<ILogger<NotificationWorker>>();
        _channel = Channel.CreateUnbounded<IPack>();
        _channel2 = Channel.CreateUnbounded<IPack>();
        _configuration = new Configuration(_channel);
        _configuration2 = new Configuration(_channel2);
        _mediatorMock = new Mock<MediatorTest>() { CallBase = true };
        _terminator = Mock.Of<ITerminator>();
        _worker = new NotificationWorker(_configuration, _terminator, _loggerMock.Object);
        _worker2 = new NotificationWorker(
            _configuration2,
            _terminator,
            _loggerMock.Object
        );
    }

    private static Pack<TMessage> Pack<TMessage>(
        TMessage message,
        NotificationHandlerDelegate<TMessage> notifier
    ) where TMessage : notnull, INotification => new(message, notifier);

    [Fact]
    public async Task ExecuteAsync_ProcessesMessages_Successfully()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message, _mediatorMock.Object.DispatchNotifications);
        await _channel.Writer.WriteAsync(pack, testCancellationToken);
        _channel.Writer.Complete();
        var cts = new CancellationTokenSource();

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100, testCancellationToken); // Allow time for processing
        await _worker.StopAsync(testCancellationToken);

        // Assert
        _mediatorMock.Verify(m => m.DispatchNotifications(message, It.IsAny<CancellationToken>()), Times.Once);
        VerifyDebugLog("Processing message of type TestMessage", Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsNullMessages()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        // Arrange
        await _channel.Writer.WriteAsync(null!, testCancellationToken);
        _channel.Writer.Complete();
        var cts = new CancellationTokenSource();

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100, testCancellationToken); // Allow time for processing
        await _worker.StopAsync(testCancellationToken);

        // Assert
        _mediatorMock.Verify(
            m => m.DispatchNotifications(It.IsAny<INotification>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ExecuteAsync_HandlesOperationCanceledException()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message, _mediatorMock.Object.DispatchNotifications);
        _mediatorMock
            .Setup(m => m.DispatchNotifications(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var cts = new CancellationTokenSource();

        await _channel.Writer.WriteAsync(pack, testCancellationToken);

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100, testCancellationToken); // Allow time for processing

        // Assert
        VerifyDebugLog(
            "An error occurred while processing message of type TestMessage",
            Times.Once(),
            LogLevel.Trace
        );
        await _worker.StopAsync(testCancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesChannelClosedException()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message, _mediatorMock.Object.DispatchNotifications);
        _mediatorMock
            .Setup(m => m.DispatchNotifications(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelClosedException());
        var cts = new CancellationTokenSource();

        await _channel.Writer.WriteAsync(pack, testCancellationToken);

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100, testCancellationToken);

        // Assert
        VerifyDebugLog(
            "An error occurred while processing message of type TestMessage",
            Times.Once(),
            LogLevel.Trace
        );
        await _worker.StopAsync(testCancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenCancellationRequested()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message, _mediatorMock.Object.DispatchNotifications);
        await _channel2.Writer.WriteAsync(pack, testCancellationToken);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        // Act
        await _worker2.StartAsync(cts.Token);
        await Task.Delay(500, testCancellationToken);

        // Assert
        VerifyDebugLog("Notification worker stopped.", Times.Once());
    }

    private void VerifyDebugLog(
        string messageContains,
        Times times,
        LogLevel level = LogLevel.Debug
    )
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

    public class TestMessage : INotification
    {
        public int Id { get; set; }
    }

    public class MediatorTest : IMediator, INotifiable
    {
        public async ValueTask Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull, INotification
        {
            await Task.WhenAll(messages.Select(m => Notify(m, cancellationToken).AsTask()));
        }

        ValueTask IMediator.Send<TMessage>(TMessage command, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DispatchNotifications<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull, INotification
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask Notify<TMessage>(TMessage notification, CancellationToken cancellationToken = default) where TMessage : notnull, INotification
        {
            return DispatchNotifications(notification, cancellationToken);
        }

        public ValueTask<TResponse> Request<TMessage, TResponse>(TMessage request, CancellationToken cancellationToken = default) where TMessage : notnull, IRequest<TResponse>
        {
            return ValueTask.FromResult(default(TResponse)!);
        }

        public async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage request, [EnumeratorCancellation] CancellationToken cancellationToken = default) where TMessage : notnull, IStream<TResponse>
        {
            yield break;
        }
    }
}
