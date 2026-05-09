using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace NetMediate.SourceGeneration;

internal readonly record struct ProcessHandlerInterfaceArguments(
    string InterfaceName,
    int Arity,
    ImmutableArray<ITypeSymbol> Args,
    bool HasDiagnostics,
    bool HasResilience,
    Dictionary<string, bool> DiagnosticsBehaviors,
    Dictionary<string, bool> ResilienceBehaviors,
    string Coverage
)
{
    public static implicit operator (
        string interfaceName,
        int arity,
        ImmutableArray<ITypeSymbol> args,
        bool hasDiagnostics,
        bool hasResilience,
        Dictionary<string, bool> diagnosticsBehaviors,
        Dictionary<string, bool> resilienceBehaviors,
        string coverage
    )(ProcessHandlerInterfaceArguments args)
    {
        return (
            args.InterfaceName,
            args.Arity,
            args.Args,
            args.HasDiagnostics,
            args.HasResilience,
            args.DiagnosticsBehaviors,
            args.ResilienceBehaviors,
            args.Coverage
        );
    }

    public static implicit operator ProcessHandlerInterfaceArguments(
        (
            string interfaceName,
            int arity,
            ImmutableArray<ITypeSymbol> args,
            bool hasDiagnostics,
            bool hasResilience,
            Dictionary<string, bool> diagnosticsBehaviors,
            Dictionary<string, bool> resilienceBehaviors,
            string coverage
        ) arguments
    )
    {
        return new(
            arguments.interfaceName,
            arguments.arity,
            arguments.args,
            arguments.hasDiagnostics,
            arguments.hasResilience,
            arguments.diagnosticsBehaviors,
            arguments.resilienceBehaviors,
            arguments.coverage
        );
    }
}
