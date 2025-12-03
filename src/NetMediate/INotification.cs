namespace NetMediate;

/// <summary>
/// Defines a notification message of type <typeparamref name="TMessage"/>.
/// </summary>
/// <typeparam name="TMessage"></typeparam>
public interface INotification<in TMessage> where TMessage : INotification<TMessage>;
