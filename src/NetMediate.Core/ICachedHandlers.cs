using GenDI;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace NetMediate;

/// <summary>
/// Represents a service that provides cached lookup of command, notification, request, and stream handlers by message
/// type and an optional key.
/// </summary>
/// <remarks>Implementations return immutable snapshots suitable for concurrent enumeration. The optional key
/// filters handlers; a null key selects handlers registered without a key. Implementations may throw when a required
/// request handler cannot be resolved.</remarks>
[ServiceInjection(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None, RegistrationEmission = RegistrationEmissionStrategy.TryAdd, RegistrationMultiplicity = RegistrationMultiplicity.Single)]
public interface ICachedHandlers
{
    /// <summary>
    /// Returns the command handlers registered for the specified message type and optional key.
    /// </summary>
    /// <typeparam name="TMessage">The message type handled by the returned command handlers.</typeparam>
    /// <param name="key">An optional key that selects a subset of handlers; null selects handlers registered without a key.</param>
    /// <returns>An immutable array of ICommandHandler{TMessage} instances that match the message type and key; empty if none are
    /// found.</returns>
    public ImmutableArray<ICommandHandler<TMessage>> GetCommandHandlers<TMessage>(object? key)
        where TMessage : notnull;

    /// <summary>
    /// Gets the notification handlers for the specified message type and optional key.
    /// </summary>
    /// <remarks>The returned array is a snapshot of the current handlers and is safe to enumerate without
    /// external synchronization.</remarks>
    /// <typeparam name="TMessage">The message type for which to retrieve handlers.</typeparam>
    /// <param name="key">An optional key used to filter the handlers; may be null.</param>
    /// <returns>An immutable array of INotificationHandler{TMessage} instances that match the specified type and key.</returns>
    public ImmutableArray<INotificationHandler<TMessage>> GetNotifyHandlers<TMessage>(object? key)
        where TMessage : notnull;

    /// <summary>
    /// Retrieves the request handler for the specified message and response types.
    /// </summary>
    /// <remarks>Handler selection is based on the message type and optional key. Implementations may throw if
    /// no suitable handler is available.</remarks>
    /// <typeparam name="TMessage">The non-nullable message type to handle.</typeparam>
    /// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
    /// <param name="key">An optional key used to resolve a specific handler; null to select the default handler.</param>
    /// <returns>The resolved IRequestHandler{TMessage, TResponse} for the specified types.</returns>
    public IRequestHandler<TMessage, TResponse> GetRequestHandler<TMessage, TResponse>(object? key)
        where TMessage : notnull;

    /// <summary>
    /// Gets the stream handlers registered for the specified key.
    /// </summary>
    /// <remarks>The returned array is immutable and safe for concurrent enumeration.</remarks>
    /// <typeparam name="TMessage">The message type handled by the returned handlers.</typeparam>
    /// <typeparam name="TResponse">The response type produced by the returned handlers.</typeparam>
    /// <param name="key">An optional key used to select handlers; pass null to retrieve handlers registered without a key.</param>
    /// <returns>An immutable array of IStreamHandler{TMessage, TResponse} that match the given key; empty if none are
    /// registered.</returns>
    public ImmutableArray<IStreamHandler<TMessage, TResponse>> GetStreamHandlers<TMessage, TResponse>(object? key)
        where TMessage : notnull;
}
