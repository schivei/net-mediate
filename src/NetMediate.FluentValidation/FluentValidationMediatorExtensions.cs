using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

namespace NetMediate.FluentValidation;

/// <summary>
/// Provides extension methods on <see cref="IMediatorServiceBuilder"/> to register
/// FluentValidation validators into the NetMediate validation pipeline.
/// </summary>
public static class FluentValidationMediatorExtensions
{
    /// <summary>
    /// Registers a FluentValidation <typeparamref name="TValidator"/> for
    /// <typeparamref name="TMessage"/> so that all NetMediate messages of that type are
    /// validated by the FluentValidation rules before being dispatched to their handler.
    /// </summary>
    /// <typeparam name="TMessage">The message type to validate.</typeparam>
    /// <typeparam name="TValidator">
    /// The FluentValidation validator type that implements <see cref="IValidator{T}"/> for
    /// <typeparamref name="TMessage"/>.
    /// </typeparam>
    /// <param name="builder">The mediator service builder.</param>
    /// <returns>The same builder to allow method chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddNetMediate()
    ///     .AddFluentValidation&lt;CreateUserCommand, CreateUserCommandValidator&gt;();
    /// </code>
    /// </example>
    public static IMediatorServiceBuilder AddFluentValidation<TMessage, TValidator>(
        this IMediatorServiceBuilder builder)
        where TValidator : class, IValidator<TMessage>
    {
        // Register the underlying FluentValidation validator so that
        // FluentValidationHandler<TMessage> can resolve it from DI.
        builder.Services.AddScoped<IValidator<TMessage>, TValidator>();

        // Register the NetMediate validation handler that wraps the FluentValidation validator.
        builder.RegisterValidationHandler<TMessage, FluentValidationHandler<TMessage>>();

        // Register FluentValidationHandler itself as a scoped service so DI can create it.
        builder.Services.AddScoped<FluentValidationHandler<TMessage>>();

        return builder;
    }

    /// <summary>
    /// Scans the assemblies already registered in the DI container and registers
    /// <see cref="FluentValidationHandler{TMessage}"/> wrappers for every
    /// <see cref="IValidator{T}"/> implementation found.
    /// </summary>
    /// <remarks>
    /// Use this overload when validators are registered separately (for example via
    /// <c>FluentValidation.DependencyInjectionExtensions</c>) and you only need the
    /// NetMediate bridge to be set up.  The individual <see cref="IValidator{T}"/>
    /// registrations must already be present in <see cref="IMediatorServiceBuilder.Services"/>
    /// before this method is called.
    /// </remarks>
    /// <param name="builder">The mediator service builder.</param>
    /// <returns>The same builder to allow method chaining.</returns>
    public static IMediatorServiceBuilder AddFluentValidationFromRegisteredValidators(
        this IMediatorServiceBuilder builder)
    {
        // Collect all IValidator<T> registrations that are already in the service collection.
        var validatorRegistrations = builder.Services
            .Where(sd =>
                sd.ServiceType.IsGenericType &&
                sd.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(sd => sd.ServiceType.GenericTypeArguments[0])
            .Distinct()
            .ToList();

        foreach (var messageType in validatorRegistrations)
        {
            var handlerType = typeof(FluentValidationHandler<>).MakeGenericType(messageType);

            // RegisterValidationHandler marks the message type as validatable so that
            // Mediator.ValidateMessage() does not short-circuit and skip FluentValidation.
            builder.RegisterValidationHandler(messageType, handlerType);
            builder.Services.AddScoped(handlerType);
        }

        return builder;
    }
}
