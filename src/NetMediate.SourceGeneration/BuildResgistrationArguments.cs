using Microsoft.CodeAnalysis;

namespace NetMediate.SourceGeneration;

internal readonly record struct BuildRegistrationArguments(
    bool HasDiagnostics,
    bool HasResilience,
    Dictionary<string, bool> DiagnosticsBehaviors,
    Dictionary<string, bool> ResilienceBehaviors,
    INamedTypeSymbol HandlerType,
    string Coverage
)
{
    public static implicit operator (
        bool hasDiagnostics,
        bool hasResilience,
        Dictionary<string, bool> diagnosticsBehaviors,
        Dictionary<string, bool> resilienceBehaviors,
        INamedTypeSymbol handlerType,
        string coverage
    )(BuildRegistrationArguments args)
    {
        return (
            args.HasDiagnostics,
            args.HasResilience,
            args.DiagnosticsBehaviors,
            args.ResilienceBehaviors,
            args.HandlerType,
            args.Coverage
        );
    }

    public static implicit operator BuildRegistrationArguments(
        (
            bool hasDiagnostics,
            bool hasResilience,
            Dictionary<string, bool> diagnosticsBehaviors,
            Dictionary<string, bool> resilienceBehaviors,
            INamedTypeSymbol handlerType,
            string coverage
        ) arguments
    )
    {
        return new(
            arguments.hasDiagnostics,
            arguments.hasResilience,
            arguments.diagnosticsBehaviors,
            arguments.resilienceBehaviors,
            arguments.handlerType,
            arguments.coverage
        );
    }
}
