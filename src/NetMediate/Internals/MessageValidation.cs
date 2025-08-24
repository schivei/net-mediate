using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal static class MessageValidation
{
    public static async Task ValidateMessageAsync<TMessage>(
        this Configuration configuration,
        IServiceScope scope,
        TMessage message,
        ILogger logger,
        Func<IServiceScope, object, bool, IEnumerable<IValidationHandler<TMessage>>> resolver,
        CancellationToken cancellationToken
    )
    {
        configuration.ThrowIfNull(message);
        configuration.LogIfNull(logger, message);

        if (message is null)
            return;

        await message.ValidatableValidationAsync();

        var handlers = resolver(scope, message, true);

        foreach (var handler in handlers)
            await handler.ValidateMessageAsync(message, cancellationToken);
    }

    private static void ThrowIfNull<TMessage>(this Configuration configuration, TMessage message)
    {
        if (!configuration.IgnoreUnhandledMessages)
            ThrowHelper.ThrowIfNull(message);
    }

    private static void LogIfNull<TMessage>(
        this Configuration configuration,
        ILogger logger,
        TMessage message
    )
    {
        if (configuration.LogUnhandledMessages && message is null)
            logger.Log(
                configuration.UnhandledMessagesLogLevel,
                "Received null message. This may indicate a misconfiguration or an error in the message pipeline."
            );
    }

    private static async Task ValidatableValidationAsync<TMessage>(this TMessage message)
    {
        if (message is IValidatable validatable)
        {
            var validationResult = await validatable.ValidateAsync();
            if (validationResult?.ErrorMessage is not null)
                throw new MessageValidationException(validationResult.ErrorMessage);
        }
    }

    private static async Task ValidateMessageAsync<TMessage>(
        this IValidationHandler<TMessage> validator,
        TMessage message,
        CancellationToken cancellationToken
    )
    {
        var validationResult = await validator.ValidateAsync(message, cancellationToken);
        if (validationResult?.ErrorMessage is not null)
            throw new MessageValidationException(validationResult.ErrorMessage);
    }
}
