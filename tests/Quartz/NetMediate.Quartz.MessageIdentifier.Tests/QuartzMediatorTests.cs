namespace NetMediate.Quartz.MessageIdentifier.Tests;

public sealed class MessageIdentifierTests
{
    [Fact]
    public async Task Handle()
    {
        var fixture = new MessageIdentifierFixture();
        await fixture.InitializeAsync();

        var method = GetType().Name + "::" + nameof(Handle);
        var counter = new CountdownEvent(10);
        fixture.TestNotifier.RegisterSemaphore(method, counter);

        QuartzMessage[] msgs = [
            new(1, method, "1"),
            new(2, method, "2"),
            new(3, method, "3", "group"),
            new(1, method, "1"),
            new(2, method, "2"),
            new(3, method, "3", "group"),
            new(4, method, null, "group")
        ];

        var solo = new TestMessage(0, method);
        var solo2 = new QuartzMessage(0, method);

        fixture.Mediator.Notify(solo);
        fixture.Mediator.Notify(solo2);

        fixture.Mediator.Notifies("keyed", msgs);
        fixture.Mediator.Notifies(msgs);

        counter.Wait(TestContext.Current.CancellationToken);

        Assert.True(20 >= fixture.TestNotifier.GetValue(method));
        Assert.True(8 <= fixture.TestNotifier.GetValue(method));
    }
}
