using Microsoft.CodeAnalysis;

namespace NetMediate.SourceGeneration;

internal readonly record struct BuildFrameworkBehaviorArguments(
    string ResilienceBehaviorTemplate,
    string DiagnosticBehaviorTemplate,
    string AssemblyName,
    bool HasDiagnostics,
    bool HasResilience,
    INamedTypeSymbol HandlerType
)
{
    public static implicit operator (
        string resilienceBehaviorTemplate,
        string diagnosticBehaviorTemplate,
        string assemblyName,
        bool hasDiagnostics,
        bool hasResilience,
        INamedTypeSymbol handlerType
    )(BuildFrameworkBehaviorArguments args)
    {
        return (
            args.ResilienceBehaviorTemplate,
            args.DiagnosticBehaviorTemplate,
            args.AssemblyName,
            args.HasDiagnostics,
            args.HasResilience,
            args.HandlerType
        );
    }

    public static implicit operator BuildFrameworkBehaviorArguments(
        (
            string resilienceBehaviorTemplate,
            string diagnosticBehaviorTemplate,
            string assemblyName,
            bool hasDiagnostics,
            bool hasResilience,
            INamedTypeSymbol handlerType
        ) arguments
    )
    {
        return new(
            arguments.resilienceBehaviorTemplate,
            arguments.diagnosticBehaviorTemplate,
            arguments.assemblyName,
            arguments.hasDiagnostics,
            arguments.hasResilience,
            arguments.handlerType
        );
    }
}
