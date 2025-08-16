using System.Threading.Channels;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public class ConfigurationTests
{
    private readonly Channel<object> _channel;
    private readonly Configuration _configuration;

    public ConfigurationTests()
    {
        _channel = Channel.CreateUnbounded<object>();
        _configuration = new Configuration(_channel);
    }

    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Assert
        Assert.Same(_channel.Writer, _configuration.ChannelWriter);
        Assert.Same(_channel.Reader, _configuration.ChannelReader);
        Assert.False(_configuration.IgnoreUnhandledMessages);
        Assert.False(_configuration.LogUnhandledMessages);
        Assert.Equal(default, _configuration.UnhandledMessagesLogLevel);
    }

    [Fact]
    public void InstantiateHandlerByMessageFilter_ThrowsException_WhenFilterIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _configuration.InstantiateHandlerByMessageFilter<TestMessage>(null!)
        );
    }

    [Fact]
    public void InstantiateHandlerByMessageFilter_RegistersFilter_WhenFilterIsValid()
    {
        // Act
        _configuration.InstantiateHandlerByMessageFilter<TestMessage>(Filter);
        var result = _configuration.TryGetHandlerTypeByMessageFilter(
            new TestMessage { Id = 1 },
            out var handlerType
        );

        // Assert
        Assert.True(result);
        Assert.Equal(typeof(TestHandler), handlerType);
    }

    private static Type? Filter(TestMessage m) => m.Id > 0 ? typeof(TestHandler) : null;

    [Fact]
    public void TryGetHandlerTypeByMessageFilter_ReturnsFalse_WhenNoFilterExists()
    {
        // Act
        var result = _configuration.TryGetHandlerTypeByMessageFilter(
            new TestMessage(),
            out var handlerType
        );

        // Assert
        Assert.False(result);
        Assert.Null(handlerType);
    }

    [Fact]
    public void TryGetHandlerTypeByMessageFilter_ReturnsCorrectHandlerType_WhenFilterExists()
    {
        // Arrange
        _configuration.InstantiateHandlerByMessageFilter<TestMessage>(m =>
            m.Id > 0 ? typeof(TestHandler) : null
        );
        var message = new TestMessage { Id = 5 };

        // Act
        var result = _configuration.TryGetHandlerTypeByMessageFilter(message, out var handlerType);

        // Assert
        Assert.True(result);
        Assert.Equal(typeof(TestHandler), handlerType);
    }

    [Fact]
    public void TryGetHandlerTypeByMessageFilter_ReturnsNullHandlerType_WhenFilterReturnsNull()
    {
        // Arrange
        _configuration.InstantiateHandlerByMessageFilter<TestMessage>(m =>
            m.Id > 0 ? typeof(TestHandler) : null
        );
        var message = new TestMessage { Id = -5 };

        // Act
        var result = _configuration.TryGetHandlerTypeByMessageFilter(message, out var handlerType);

        // Assert
        Assert.True(result);
        Assert.Null(handlerType);
    }

    [Fact]
    public async Task DisposeAsync_CompletesWriterAndDrainsReader()
    {
        // Arrange
        await _configuration.ChannelWriter.WriteAsync(new TestMessage());

        // Act
        await _configuration.DisposeAsync();

        // Assert
        Assert.False(await _configuration.ChannelReader.WaitToReadAsync());
    }

    // Test classes
    public class TestMessage
    {
        public int Id { get; set; }
    }

    public class TestHandler { }
}
