namespace NetMediate.Internals;

internal interface INotifiable
{
    Task Notifies(object message, CancellationToken cancellationToken = default);
}
