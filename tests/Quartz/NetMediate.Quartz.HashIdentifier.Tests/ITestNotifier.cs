using System.Collections.Concurrent;

namespace NetMediate.Quartz.HashIdentifier.Tests;

public interface ITestNotifier
{
    void CheckValueFor(string method, int value);
    void RegisterSemaphore(string method, CountdownEvent cts);
    int GetValue(string method);
}

public class TestNotifier : ITestNotifier
{
    private readonly ConcurrentDictionary<string, int> _methodValues = new();
    private readonly ConcurrentDictionary<string, CountdownEvent> _methodSemaphores = new();

    public void CheckValueFor(string method, int value)
    {
        _methodValues.AddOrUpdate(method, value, (_, current) => current + value);
        if (_methodSemaphores.TryGetValue(method, out var cts))
            cts.Signal();
    }

    public void RegisterSemaphore(string method, CountdownEvent cts)
    {
        _methodValues.AddOrUpdate(method, 0, (_, current) => current);
        _methodSemaphores[method] = cts;
    }

    public int GetValue(string method)
    {
        return _methodValues.TryGetValue(method, out var value) ? value : 0;
    }
}
