using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public sealed class NotificationPacketTests
{
    [Fact]
    public async Task OnErrorAsync_WithoutHandler_Completes()
    {
        var p = new NotificationPacket<int>(7);
        await p.OnErrorAsync(typeof(NotificationPacketTests), new Exception("boom"));
        // No exception expected
    }

    [Fact]
    public async Task OnErrorAsync_WithHandler_ReceivesArgs()
    {
        var called = false;
        NotificationErrorDelegate<string> onError = (t, msg, ex) =>
        {
            Assert.Equal(typeof(NotificationPacketTests), t);
            Assert.Equal("hi", msg);
            Assert.Equal("x", ex.Message);
            called = true;
            return Task.CompletedTask;
        };
        var p = new NotificationPacket<string>("hi", onError);
        await p.OnErrorAsync(typeof(NotificationPacketTests), new Exception("x"));
        Assert.True(called);
    }
}