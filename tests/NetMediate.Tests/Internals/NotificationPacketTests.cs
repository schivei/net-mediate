using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public sealed class NotificationPacketTests
{
    [Fact]
    public void NotificationPacket_StoresMessage()
    {
        var packet = new NotificationPacket<int>(42);
        Assert.Equal(42, packet.Message);
        Assert.Equal(42, ((INotificationPacket)packet).Message);
    }
}
