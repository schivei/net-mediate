using System.ComponentModel.DataAnnotations;

namespace NetMediate.Internals;

internal readonly struct Pack<TMessage>(TMessage message, MessageHandlerDelegate<TMessage> handler, MessageValidationDelegate<TMessage>? validator = null) : IPack
{
    public ValueTask Dispatch(CancellationToken cancellationToken = default) =>
        handler(message, cancellationToken);
    
    public ValueTask<ValidationResult> ValidateAsync(CancellationToken cancellationToken) =>
        Extensions.ValidateAsync(message, validator, cancellationToken);
}

internal readonly struct Pack<TMessage, TResult>(TMessage message, MessageHandlerDelegate<TMessage, TResult> handler, MessageValidationDelegate<TMessage>? validator = null) : IPack<TResult> where TResult : notnull
{
    public TResult Dispatch(CancellationToken cancellationToken = default) =>
        handler(message, cancellationToken);
    
    public ValueTask<ValidationResult> ValidateAsync(CancellationToken cancellationToken) =>
        Extensions.ValidateAsync(message, validator, cancellationToken);
}
