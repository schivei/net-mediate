using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace NetMediate.SourceGeneration;

internal readonly record struct ProcessHandlerInterfaceArguments(string InterfaceName,
        int Arity,
        string HandlerName,
        string HandlerKeyArgument,
        ImmutableArray<ITypeSymbol> Args,
        bool HasDiagnostics,
        bool HasResilience,
        Dictionary<string, bool> DiagnosticsBehaviors,
        Dictionary<string, bool> ResilienceBehaviors,
        Dictionary<string, bool> Handlers)
{
    public static implicit operator (string interfaceName, int arity, string handlerName, string handlerKeyArgument, ImmutableArray<ITypeSymbol> args, bool hasDiagnostics, bool hasResilience, Dictionary<string, bool> diagnosticsBehaviors, Dictionary<string, bool> resilienceBehaviors, Dictionary<string, bool> handlers)(ProcessHandlerInterfaceArguments args)
    {
        return (args.InterfaceName,
        args.Arity,
        args.HandlerName,
        args.HandlerKeyArgument,
        args.Args,
        args.HasDiagnostics,
        args.HasResilience,
        args.DiagnosticsBehaviors,
        args.ResilienceBehaviors,
        args.Handlers);
    }

    public static implicit operator ProcessHandlerInterfaceArguments((string interfaceName, int arity, string handlerName, string handlerKeyArgument, ImmutableArray<ITypeSymbol> args, bool hasDiagnostics, bool hasResilience, Dictionary<string, bool> diagnosticsBehaviors, Dictionary<string, bool> resilienceBehaviors, Dictionary<string, bool> handlers) arguments)
    {
        return new(arguments.interfaceName,
        arguments.arity,
        arguments.handlerName,
        arguments.handlerKeyArgument,
        arguments.args,
        arguments.hasDiagnostics,
        arguments.hasResilience,
        arguments.diagnosticsBehaviors,
        arguments.resilienceBehaviors,
        arguments.handlers);
    }
}
