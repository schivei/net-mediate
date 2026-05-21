namespace NetMediate.Quartz.HashIdentifier.Tests;

public sealed class HashIdentifierTests
{
    [Fact]
    public async Task Handle()
    {
        var fixture = new HashIdentifierFixture();
        await fixture.InitializeAsync();

        var method = GetType().Name + "::" + nameof(Handle);
        var counter = new CountdownEvent(8);
        fixture.TestNotifier.RegisterSemaphore(method, counter);

        TestMessage[] msgs = [
            new(1, method),
            new(2, method),
            new(3, method)
        ];

        var solo = new TestMessage(0, method);

        fixture.Mediator.Notify("keyed", solo);
        fixture.Mediator.Notifies<TestMessage>("keyed", []);
        fixture.Mediator.Notifies("keyed", msgs);

        fixture.Mediator.Notify(solo);
        fixture.Mediator.Notifies<TestMessage>([]);
        fixture.Mediator.Notifies(msgs);

        counter.Wait(TestContext.Current.CancellationToken);

        Assert.True(12 >= fixture.TestNotifier.GetValue(method));
        Assert.True(8 <= fixture.TestNotifier.GetValue(method));
    }
}
