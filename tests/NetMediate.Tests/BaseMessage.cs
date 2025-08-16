namespace NetMediate.Tests;

internal abstract record BaseMessage
{
    public bool Runned { get; private set; }

    public void Run() => Runned = true;
}
