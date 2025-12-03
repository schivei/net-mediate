namespace NetMediate;

/// <summary>
/// Defines a command message of type <typeparamref name="TMessage"/>.
/// </summary>
/// <typeparam name="TMessage"></typeparam>
public interface ICommand<in TMessage> where TMessage : ICommand<TMessage>;
