using System.Text;
using Microsoft.CodeAnalysis;

namespace NetMediate.SourceGeneration;

internal readonly record struct BuildRegistrationArguments(bool HasDiagnostics, bool HasResilience, Dictionary<string, bool> DiagnosticsBehaviors, Dictionary<string, bool> ResilienceBehaviors, Dictionary<string, bool> Handlers, StringBuilder Notifier, INamedTypeSymbol HandlerType, string HandlerName, string HandlerKeyArgument)
{
    public static implicit operator (bool hasDiagnostics, bool hasResilience, Dictionary<string, bool> diagnosticsBehaviors, Dictionary<string, bool> resilienceBehaviors, Dictionary<string, bool> handlers, StringBuilder notifier, INamedTypeSymbol handlerType, string handlerName, string handlerKeyArgument)(BuildRegistrationArguments args)
    {
        return (args.HasDiagnostics, args.HasResilience, args.DiagnosticsBehaviors, args.ResilienceBehaviors, args.Handlers, args.Notifier, args.HandlerType, args.HandlerName, args.HandlerKeyArgument);
    }

    public static implicit operator BuildRegistrationArguments((bool hasDiagnostics, bool hasResilience, Dictionary<string, bool> diagnosticsBehaviors, Dictionary<string, bool> resilienceBehaviors, Dictionary<string, bool> handlers, StringBuilder notifier, INamedTypeSymbol handlerType, string handlerName, string handlerKeyArgument) arguments)
    {
        return new(arguments.hasDiagnostics, arguments.hasResilience, arguments.diagnosticsBehaviors, arguments.resilienceBehaviors, arguments.handlers, arguments.notifier, arguments.handlerType, arguments.handlerName, arguments.handlerKeyArgument);
    }
}
