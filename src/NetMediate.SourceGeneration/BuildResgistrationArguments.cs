using Microsoft.CodeAnalysis;

namespace NetMediate.SourceGeneration;

internal readonly record struct BuildRegistrationArguments(
    string BehaviorTemplate,
    string AssemblyName,
    bool HasDiagnostics,
    bool HasResilience,
    INamedTypeSymbol HandlerType
)
{
    public static implicit operator (
        string behaviorTemplate,
        string assemblyName,
        bool hasDiagnostics,
        bool hasResilience,
        INamedTypeSymbol handlerType
    )(BuildRegistrationArguments args)
    {
        return (
            args.BehaviorTemplate,
            args.AssemblyName,
            args.HasDiagnostics,
            args.HasResilience,
            args.HandlerType
        );
    }

    public static implicit operator BuildRegistrationArguments(
        (
            string behaviorTemplate,
            string assemblyName,
            bool hasDiagnostics,
            bool hasResilience,
            INamedTypeSymbol handlerType
        ) arguments
    )
    {
        return new(
            arguments.behaviorTemplate,
            arguments.assemblyName,
            arguments.hasDiagnostics,
            arguments.hasResilience,
            arguments.handlerType
        );
    }
}
