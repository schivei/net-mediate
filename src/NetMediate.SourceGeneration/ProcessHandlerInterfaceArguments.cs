using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace NetMediate.SourceGeneration;

internal readonly record struct ProcessHandlerInterfaceArguments(
    string BehaviorTemplate,
    string AssemblyName,
    string InterfaceName,
    int Arity,
    ImmutableArray<ITypeSymbol> Args,
    bool HasDiagnostics,
    bool HasResilience
)
{
    public static implicit operator (
        string behaviorTemplate,
        string assemblyName,
        string interfaceName,
        int arity,
        ImmutableArray<ITypeSymbol> args,
        bool hasDiagnostics,
        bool hasResilience
    )(ProcessHandlerInterfaceArguments args)
    {
        return (
            args.BehaviorTemplate,
            args.AssemblyName,
            args.InterfaceName,
            args.Arity,
            args.Args,
            args.HasDiagnostics,
            args.HasResilience
        );
    }

    public static implicit operator ProcessHandlerInterfaceArguments(
        (
            string behaviorTemplate,
            string assemblyName,
            string interfaceName,
            int arity,
            ImmutableArray<ITypeSymbol> args,
            bool hasDiagnostics,
            bool hasResilience
        ) arguments
    )
    {
        return new(
            arguments.behaviorTemplate,
            arguments.assemblyName,
            arguments.interfaceName,
            arguments.arity,
            arguments.args,
            arguments.hasDiagnostics,
            arguments.hasResilience
        );
    }
}
