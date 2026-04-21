using System.Reflection;
using NetMediate;

namespace MediatR;

/// <summary>
/// Configures assembly scanning for MediatR-compatible registrations.
/// </summary>
public sealed class MediatRServiceConfiguration
{
    internal HashSet<Assembly> AssembliesToRegister { get; } = [];

    /// <summary>
    /// Registers handlers from the provided assembly.
    /// </summary>
    /// <param name="assembly">Assembly to scan.</param>
    /// <returns>Current configuration instance.</returns>
    public MediatRServiceConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        Guard.ThrowIfNull(assembly);

        AssembliesToRegister.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers handlers from the assembly containing <typeparamref name="TMarker"/>.
    /// </summary>
    /// <typeparam name="TMarker">Marker type for assembly lookup.</typeparam>
    /// <returns>Current configuration instance.</returns>
    public MediatRServiceConfiguration RegisterServicesFromAssemblyContaining<TMarker>() =>
        RegisterServicesFromAssembly(typeof(TMarker).Assembly);

    /// <summary>
    /// Registers handlers from the provided assemblies.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan.</param>
    /// <returns>Current configuration instance.</returns>
    public MediatRServiceConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        Guard.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
            RegisterServicesFromAssembly(assembly);

        return this;
    }
}
