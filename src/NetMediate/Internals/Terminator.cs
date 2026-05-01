namespace NetMediate.Internals;

internal class Terminator(Action termination) : ITerminator
{
    public void Terminate() => termination();
}
