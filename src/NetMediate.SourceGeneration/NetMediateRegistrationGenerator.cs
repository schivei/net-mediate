using System.IO;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetMediate.SourceGeneration;

[Generator]
public sealed class NetMediateRegistrationGenerator : IIncrementalGenerator
{
    private const string NotifierToken = "{{Notifier}}";
    private const string RegistrationsToken = "{{Registrations}}";
    private const string FrameworkBehaviorsToken = "{{FrameworkBehaviors}}";

    // Resolved once; format: <RootNamespace>.<FileName>
    private static readonly string TemplateResourceName =
        $"{typeof(NetMediateRegistrationGenerator).Namespace}.NetMediateGeneratedDI.template";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlerTypes = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds && cds.BaseList is not null,
                transform: static (ctx, _) =>
                {
                    if (ctx.Node is not ClassDeclarationSyntax declaration)
                        return null;

                    if (ctx.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol typeSymbol)
                        return null;

                    if (typeSymbol.IsAbstract || typeSymbol.IsGenericType)
                        return null;

                    if (!IsAccessible(typeSymbol))
                        return null;

                    return typeSymbol;
                }
            )
            .Where(static t => t is not null)
            .Select(static (t, _) => t!)
            .Collect();

        // Detect referenced framework packages.
        // Diagnostics must be registered FIRST (outermost in the pipeline after Reverse/Aggregate).
        // Resilience is registered SECOND.
        // Both are registered per message-type using closed-type RegisterBehavior<> calls —
        // no open-generic typeof() registrations are emitted.
        var packageInfo = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var names = compilation.ReferencedAssemblyNames;
            bool hasDiagnostics = false;
            bool hasResilience = false;

            foreach (var assemblyName in names)
            {
                if (assemblyName.Name == "NetMediate.Diagnostics")
                    hasDiagnostics = true;
                else if (assemblyName.Name == "NetMediate.Resilience")
                    hasResilience = true;
            }

            return (hasDiagnostics, hasResilience);
        });

        var combined = handlerTypes.Combine(packageInfo);

        context.RegisterSourceOutput(
            combined,
            static (sourceProductionContext, input) =>
            {
                var (types, (hasDiagnostics, hasResilience)) = input;
                var (registrations, notifier) = BuildRegistrations(types, hasDiagnostics, hasResilience);
                var frameworkBehaviors = BuildFrameworkInfrastructure(hasDiagnostics, hasResilience);
                var source = BuildSource(registrations, notifier, frameworkBehaviors);
                sourceProductionContext.AddSource(
                    "NetMediateGeneratedDI.g.cs",
                    source
                );
            }
        );
    }

    /// <summary>
    /// Builds the infrastructure setup lines that go BEFORE the AddNetMediate configure block.
    /// These calls register options (for Resilience) or serve as markers — no open-generic behavior
    /// registrations are emitted here; behaviors are registered per-handler in the configure block.
    /// </summary>
    private static string BuildFrameworkInfrastructure(bool hasDiagnostics, bool hasResilience)
    {
        const string indent = "        "; // 8 spaces
        var sb = new System.Text.StringBuilder();

        if (hasDiagnostics)
            sb.AppendLine($"{indent}services.AddNetMediateDiagnostics();");

        // Registers default options; user may have called AddNetMediateResilience(configure...) earlier.
        if (hasResilience)
            sb.AppendLine($"{indent}services.AddNetMediateResilience();");

        return sb.ToString();
    }

    /// <summary>
    /// Builds the ordered handler + behavior registrations.
    /// Output order per message-type: Diagnostics behaviors → Resilience behaviors → handler.
    /// Uses insertion-ordered dictionaries for deduplication so the same behavior is never
    /// emitted twice even when multiple handlers share the same message type.
    /// </summary>
    private static (IEnumerable<string> registrations, string notifier) BuildRegistrations(
        ImmutableArray<INamedTypeSymbol> types,
        bool hasDiagnostics,
        bool hasResilience)
    {
        // Ordered insertion + deduplication: Dictionary<key, bool> preserves insertion order in C#.
        var diagnosticsBehaviors = new Dictionary<string, bool>(StringComparer.Ordinal);
        var resilienceBehaviors  = new Dictionary<string, bool>(StringComparer.Ordinal);
        var handlers             = new Dictionary<string, bool>(StringComparer.Ordinal);
        var notifier             = "AddNetMediate";

        foreach (var handlerType in types)
        {
            var handlerName = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            foreach (var @interface in handlerType.AllInterfaces)
            {
                var definition = @interface.OriginalDefinition;
                if (definition.ContainingNamespace.ToDisplayString() != "NetMediate")
                    continue;

                var name = definition.Name;
                var arity = definition.Arity;
                var args = @interface.TypeArguments;

                if (name == "INotifier" && arity == 0)
                {
                    notifier += $"<{handlerName}>";
                    continue;
                }

                ProcessHandlerInterface(
                    name, arity, handlerName, args,
                    hasDiagnostics, hasResilience,
                    diagnosticsBehaviors, resilienceBehaviors, handlers);
            }
        }

        // Final output order: Diagnostics behaviors → Resilience behaviors → handler registrations.
        var allRegistrations = diagnosticsBehaviors.Keys
            .Concat(resilienceBehaviors.Keys)
            .Concat(handlers.Keys);

        return (allRegistrations, notifier);
    }

    private static void ProcessHandlerInterface(
        string interfaceName,
        int arity,
        string handlerName,
        ImmutableArray<ITypeSymbol> args,
        bool hasDiagnostics,
        bool hasResilience,
        Dictionary<string, bool> diagnosticsBehaviors,
        Dictionary<string, bool> resilienceBehaviors,
        Dictionary<string, bool> handlers)
    {
        if (arity == 1 && args.Length == 1)
        {
            if (!IsAccessible(args[0])) return;

            var msg = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            switch (interfaceName)
            {
                case "ICommandHandler":
                    AddCommandNotificationBehaviors(msg, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors);
                    handlers.AddIfNew($"configure.RegisterCommandHandler<{handlerName}, {msg}>();");
                    break;

                case "INotificationHandler":
                    AddCommandNotificationBehaviors(msg, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors);
                    handlers.AddIfNew($"configure.RegisterNotificationHandler<{handlerName}, {msg}>();");
                    break;
            }
        }
        else if (arity == 2 && args.Length == 2)
        {
            if (!IsAccessible(args[0]) || !IsAccessible(args[1])) return;

            var msg  = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var resp = args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            switch (interfaceName)
            {
                case "IRequestHandler":
                    AddRequestBehaviors(msg, resp, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors);
                    handlers.AddIfNew($"configure.RegisterRequestHandler<{handlerName}, {msg}, {resp}>();");
                    break;

                case "IStreamHandler":
                    AddStreamBehaviors(msg, resp, hasDiagnostics, diagnosticsBehaviors);
                    handlers.AddIfNew($"configure.RegisterStreamHandler<{handlerName}, {msg}, {resp}>();");
                    break;
            }
        }
    }

    // ── Behavior registration helpers ────────────────────────────────────────────────────────

    private static void AddCommandNotificationBehaviors(
        string msg,
        bool hasDiagnostics,
        bool hasResilience,
        Dictionary<string, bool> diag,
        Dictionary<string, bool> res)
    {
        const string task = "global::System.Threading.Tasks.Task";

        if (hasDiagnostics)
            diag.AddIfNew(
                $"configure.RegisterBehavior<global::NetMediate.Diagnostics.TelemetryNotificationBehavior<{msg}>, {msg}, {task}>();");

        if (hasResilience)
        {
            res.AddIfNew($"configure.RegisterBehavior<global::NetMediate.Resilience.RetryNotificationBehavior<{msg}>, {msg}, {task}>();");
            res.AddIfNew($"configure.RegisterBehavior<global::NetMediate.Resilience.TimeoutNotificationBehavior<{msg}>, {msg}, {task}>();");
            res.AddIfNew($"configure.RegisterBehavior<global::NetMediate.Resilience.CircuitBreakerNotificationBehavior<{msg}>, {msg}, {task}>();");
        }
    }

    private static void AddRequestBehaviors(
        string msg,
        string resp,
        bool hasDiagnostics,
        bool hasResilience,
        Dictionary<string, bool> diag,
        Dictionary<string, bool> res)
    {
        var taskResp = $"global::System.Threading.Tasks.Task<{resp}>";

        if (hasDiagnostics)
            diag.AddIfNew(
                $"configure.RegisterBehavior<global::NetMediate.Diagnostics.TelemetryRequestBehavior<{msg}, {resp}>, {msg}, {taskResp}>();");

        if (hasResilience)
        {
            res.AddIfNew($"configure.RegisterBehavior<global::NetMediate.Resilience.RetryRequestBehavior<{msg}, {resp}>, {msg}, {taskResp}>();");
            res.AddIfNew($"configure.RegisterBehavior<global::NetMediate.Resilience.TimeoutRequestBehavior<{msg}, {resp}>, {msg}, {taskResp}>();");
            res.AddIfNew($"configure.RegisterBehavior<global::NetMediate.Resilience.CircuitBreakerRequestBehavior<{msg}, {resp}>, {msg}, {taskResp}>();");
        }
    }

    private static void AddStreamBehaviors(
        string msg,
        string resp,
        bool hasDiagnostics,
        Dictionary<string, bool> diag)
    {
        if (!hasDiagnostics) return;

        var asyncEnum = $"global::System.Collections.Generic.IAsyncEnumerable<{resp}>";
        diag.AddIfNew(
            $"configure.RegisterBehavior<global::NetMediate.Diagnostics.TelemetryStreamBehavior<{msg}, {resp}>, {msg}, {asyncEnum}>();");
    }

    // ── Source building ───────────────────────────────────────────────────────────────────────

    private static string BuildSource(IEnumerable<string> registrations, string notifier, string frameworkBehaviors)
    {
        const string indent = "            ";
        var registrationsBlock = string.Join(
            "\n",
            registrations.Select(r => indent + r)
        );

        return LoadTemplate()
            .Replace(NotifierToken, notifier)
            .Replace(RegistrationsToken, registrationsBlock)
            .Replace(FrameworkBehaviorsToken, frameworkBehaviors);
    }

    private static string LoadTemplate()
    {
        var stream = typeof(NetMediateRegistrationGenerator)
            .Assembly
            .GetManifestResourceStream(TemplateResourceName);

        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded template resource '{TemplateResourceName}' was not found. " +
                "Ensure 'NetMediateGeneratedDI.template' is included as an EmbeddedResource " +
                "in the NetMediate.SourceGeneration project."
            );

        using (stream)
        using (var reader = new StreamReader(stream))
            return reader.ReadToEnd();
    }

    // ── Accessibility helpers ─────────────────────────────────────────────────────────────────

    private static bool IsAccessible(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayType)
            return IsAccessible(arrayType.ElementType);

        if (typeSymbol is IPointerTypeSymbol pointerType)
            return IsAccessible(pointerType.PointedAtType);

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.TypeArguments.Any(argument => !IsAccessible(argument)))
                return false;

            return IsNamedTypeAccessible(namedType);
        }

        return true;
    }

    private static bool IsNamedTypeAccessible(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            if (
                current.DeclaredAccessibility is not Accessibility.Public
                and not Accessibility.Internal
            )
            {
                return false;
            }
        }

        return true;
    }
}

file static class DictionaryExtensions
{
    /// <summary>
    /// Inserts <paramref name="key"/> with a <c>true</c> value if the key is not already present.
    /// Provides insertion-ordered deduplication on <c>netstandard2.0</c> where
    /// <see cref="Dictionary{TKey,TValue}.TryAdd"/> is unavailable.
    /// </summary>
    internal static void AddIfNew(this Dictionary<string, bool> dict, string key)
    {
        if (!dict.ContainsKey(key))
            dict[key] = true;
    }
}
