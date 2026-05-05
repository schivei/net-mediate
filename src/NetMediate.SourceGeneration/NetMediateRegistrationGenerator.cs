using System.Collections.Immutable;
using System.Text;
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
                predicate: static (node, _) => Search(node),
                transform: static (ctx, _) => Transform(ctx)
            )
            .Where(static t => t is not null)
            .Select(static (t, _) => t!)
            .Collect();

        var packageInfo = context.CompilationProvider.Select(static (compilation, _) => Selects(compilation));

        var combined = handlerTypes.Combine(packageInfo);

        context.RegisterSourceOutput(
            combined,
            static (sourceProductionContext, input) => Accumulate(sourceProductionContext, input)
        );
    }

    private static void Accumulate(SourceProductionContext sourceProductionContext, (ImmutableArray<INamedTypeSymbol> Left, (bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly) Right) input)
    {
        var (types, (hasDiagnostics, hasResilience, isNetMediateAssembly)) = input;

        if (isNetMediateAssembly)
        {
            sourceProductionContext.AddSource(
                "NetMediateGeneratedDI.g.cs",
                "// Source generation skipped for the NetMediate core assembly.\n" +
                "// AddNetMediate() is generated in the referencing project by the source generator.");
            return;
        }

        var (registrations, notifier) = BuildRegistrations(types, hasDiagnostics, hasResilience);
        var frameworkBehaviors = BuildFrameworkInfrastructure(hasResilience);
        var source = BuildSource(registrations, notifier, frameworkBehaviors);
        sourceProductionContext.AddSource(
            "NetMediateGeneratedDI.g.cs",
            source
        );
    }

    private static (bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly) Selects(Compilation compilation)
    {
        var names = compilation.ReferencedAssemblyNames.Select(name => name.Name);
        bool hasDiagnostics = names.Contains("NetMediate.Diagnostics");
        bool hasResilience = names.Contains("NetMediate.Resilience");
        bool isNetMediateAssembly = compilation.AssemblyName == "NetMediate";

        return (hasDiagnostics, hasResilience, isNetMediateAssembly);
    }

    private static bool Search(SyntaxNode node) =>
        node is ClassDeclarationSyntax cds && cds.BaseList is not null;

    private static INamedTypeSymbol? Transform(GeneratorSyntaxContext ctx)
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

    /// <summary>
    /// Builds the infrastructure setup lines that go BEFORE the UseNetMediate configure block.
    /// For Resilience: registers default option singletons (user may override by registering the
    /// same type before calling <c>AddNetMediate()</c>; <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton"/>
    /// skips registration when the type is already present).
    /// For Diagnostics: no infrastructure needed — <c>NetMediateDiagnostics</c> is a static class.
    /// </summary>
    private static string BuildFrameworkInfrastructure(bool hasResilience)
    {
        const string indent = "        "; // 8 spaces
        var sb = new System.Text.StringBuilder();

        // Resilience: register default options; user-supplied registrations that were added
        // earlier will be kept (TryAddSingleton is a no-op when the type is already present).
        if (hasResilience)
        {
            sb.AppendLine($"{indent}services.TryAddSingleton(new global::NetMediate.Resilience.RetryBehaviorOptions());");
            sb.AppendLine($"{indent}services.TryAddSingleton(new global::NetMediate.Resilience.TimeoutBehaviorOptions());");
            sb.AppendLine($"{indent}services.TryAddSingleton(new global::NetMediate.Resilience.CircuitBreakerBehaviorOptions());");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the ordered handler + behavior registrations.
    /// Output order per message-type: Diagnostics behaviors → Resilience behaviors → handler.
    /// Uses insertion-ordered dictionaries for deduplication so the same behavior is never
    /// emitted twice even when multiple handlers share the same message type.
    /// </summary>
    private static (IEnumerable<string> registrations, StringBuilder notifier) BuildRegistrations(
        ImmutableArray<INamedTypeSymbol> types,
        bool hasDiagnostics,
        bool hasResilience)
    {
        // Ordered insertion + deduplication: Dictionary<key, bool> preserves insertion order in C#.
        var diagnosticsBehaviors = new Dictionary<string, bool>(StringComparer.Ordinal);
        var resilienceBehaviors  = new Dictionary<string, bool>(StringComparer.Ordinal);
        var handlers             = new Dictionary<string, bool>(StringComparer.Ordinal);
        var notifier             = new StringBuilder("UseNetMediate");

        foreach (var handlerType in types)
        {
            var handlerName = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            BuildResgistration(hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors, handlers, notifier, handlerType, handlerName);
        }

        // Final output order: Diagnostics behaviors → Resilience behaviors → handler registrations.
        var allRegistrations = diagnosticsBehaviors.Keys
            .Concat(resilienceBehaviors.Keys)
            .Concat(handlers.Keys);

        return (allRegistrations, notifier);
    }

    private static void BuildResgistration(bool hasDiagnostics, bool hasResilience, Dictionary<string, bool> diagnosticsBehaviors, Dictionary<string, bool> resilienceBehaviors, Dictionary<string, bool> handlers, StringBuilder notifier, INamedTypeSymbol handlerType, string handlerName) // NOSONAR S107
    {
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
                notifier.Append($"<{handlerName}>");
                continue;
            }

            ProcessHandlerInterface(
                name, arity, handlerName, args,
                hasDiagnostics, hasResilience,
                diagnosticsBehaviors, resilienceBehaviors, handlers);
        }
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
        Dictionary<string, bool> handlers) // NOSONAR S107
    {
        AddCommand(interfaceName, arity, handlerName, args, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors, handlers);

        AddRequest(interfaceName, arity, handlerName, args, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors, handlers);
    }

    private static void AddRequest(string interfaceName, int arity, string handlerName, ImmutableArray<ITypeSymbol> args, bool hasDiagnostics, bool hasResilience, Dictionary<string, bool> diagnosticsBehaviors, Dictionary<string, bool> resilienceBehaviors, Dictionary<string, bool> handlers)  // NOSONAR S107
    {
        if (arity == 2 && args.Length == 2)
        {
            if (!IsAccessible(args[0]) || !IsAccessible(args[1])) return;

            var msg = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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

    private static void AddCommand(string interfaceName, int arity, string handlerName, ImmutableArray<ITypeSymbol> args, bool hasDiagnostics, bool hasResilience, Dictionary<string, bool> diagnosticsBehaviors, Dictionary<string, bool> resilienceBehaviors, Dictionary<string, bool> handlers)  // NOSONAR S107
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

    private static string BuildSource(IEnumerable<string> registrations, StringBuilder notifier, string frameworkBehaviors)
    {
        const string indent = "            ";
        var registrationsBlock = string.Join(
            "\n",
            registrations.Select(r => indent + r)
        );

        return LoadTemplate()
            .Replace(NotifierToken, notifier.ToString())
            .Replace(RegistrationsToken, registrationsBlock)
            .Replace(FrameworkBehaviorsToken, frameworkBehaviors);
    }

    private static string LoadTemplate()
    {
        var stream = typeof(NetMediateRegistrationGenerator)
            .Assembly
            .GetManifestResourceStream(TemplateResourceName) ?? throw new InvalidOperationException(
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
