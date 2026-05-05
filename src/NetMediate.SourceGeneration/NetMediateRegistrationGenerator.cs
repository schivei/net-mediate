using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetMediate.SourceGeneration;

[Generator]
public sealed class NetMediateRegistrationGenerator : IIncrementalGenerator
{
    private const string NotifierToken = "{{Notifier}}";
    private const string RegistrationsToken = "{{Registrations}}";
    private const string FrameworkBehaviorsToken = "{{FrameworkBehaviors}}";
    private const string AssemblyNamespaceToken = "{{AssemblyNamespace}}";
    private const string KeyedServiceAttributeMetadataName = "NetMediate.KeyedServiceAttribute";
    private const string ServiceOrderAttributeMetadataName = "NetMediate.ServiceOrderAttribute";

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

        packageInfo = Compute(packageInfo);

        var combined = handlerTypes.Combine(packageInfo);

        context.RegisterSourceOutput(
            combined,
            static (sourceProductionContext, input) => Accumulate(sourceProductionContext, input)
        );
    }

    private static readonly HashSet<string> _names = [];

    private static IncrementalValueProvider<(bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly, string assemblyName)> Compute(IncrementalValueProvider<(bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly, string assemblyName)> packageInfo)
    {
        packageInfo.Select(static (input, _) => ExtractNames(input));

        return packageInfo.Select(static (input, _) => CalculateName(input));
    }

    private static bool ExtractNames((bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly, string assemblyName) input)
    {
        lock (_names)
        {
            if (input.assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal))
                return true;

            if (input.assemblyName.StartsWith("System.", StringComparison.Ordinal))
                return true;

            if (input.assemblyName.StartsWith("NetMediate.", StringComparison.Ordinal) &&
                !input.assemblyName.Contains(".Tests", StringComparison.Ordinal) &&
                !input.assemblyName.Contains(".Benchmarks", StringComparison.Ordinal))
                return true;

            _names.Add(input.assemblyName);
        }

        return true;
    }

    private static string FindMostCommonBaseNamespace()
    {
        lock (_names)
        {
            if (_names.Count == 0)
                return "NetMediate";

            var prefixCount = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

            foreach (var ns in _names)
            {
                if (string.IsNullOrWhiteSpace(ns))
                    continue;

                var parts = ns.Split('.');

                for (int i = 1; i <= parts.Length; i++)
                {
                    var prefix = string.Join(".", parts.Take(i));
                    if (!prefixCount.TryAdd(prefix, 1))
                        prefixCount[prefix]++;
                }
            }

            var best = prefixCount
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.Count(c => c == '.'))
            .First();

            return best.Key;
        }
    }

    private static (bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly, string assemblyName) CalculateName((bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly, string) input)
    {
        var (hasDiagnostics, hasResilience, isNetMediateAssembly, _) = input;

        var assemblyName = FindMostCommonBaseNamespace();

        return (hasDiagnostics, hasResilience, isNetMediateAssembly, assemblyName);
    }

    private static void Accumulate(SourceProductionContext sourceProductionContext, (ImmutableArray<INamedTypeSymbol> Left, (bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly, string assemblyName) Right) input)
    {
        var (types, (hasDiagnostics, hasResilience, isNetMediateAssembly, assemblyName)) = input;

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
        var source = BuildSource(registrations, notifier, frameworkBehaviors, assemblyName);
        sourceProductionContext.AddSource(
            "NetMediateGeneratedDI.g.cs",
            source
        );
    }

    private static (bool hasDiagnostics, bool hasResilience, bool isNetMediateAssembly, string assemblyName) Selects(Compilation compilation)
    {
        var names = compilation.ReferencedAssemblyNames.Select(name => name.Name);
        bool hasDiagnostics = names.Contains("NetMediate.Diagnostics");
        bool hasResilience = names.Contains("NetMediate.Resilience");
        bool isNetMediateAssembly = compilation.AssemblyName == "NetMediate";

        return (hasDiagnostics, hasResilience, isNetMediateAssembly, compilation.AssemblyName);
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
        var sb = new StringBuilder();

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
        var diagnosticsBehaviors = new Dictionary<int, Dictionary<string, bool>>();
        var resilienceBehaviors  = new Dictionary<int, Dictionary<string, bool>>();
        var handlers             = new Dictionary<int, Dictionary<string, bool>>();
        var notifier             = new StringBuilder("UseNetMediate");

        foreach (var handlerType in types)
        {
            var handlerName = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var handlerKeyArgument = BuildHandlerKeyArgument(handlerType);
            var order = GetOrderArgument(handlerType);

            diagnosticsBehaviors.AddIfNew(order);
            resilienceBehaviors.AddIfNew(order);
            handlers.AddIfNew(order);

            BuildResgistration((hasDiagnostics, hasResilience, diagnosticsBehaviors[order], resilienceBehaviors[order], handlers[order], notifier, handlerType, handlerName, handlerKeyArgument));
        }

        // Final output order: Diagnostics behaviors → Resilience behaviors → handler registrations.
        var allRegistrations = diagnosticsBehaviors.OrderBy(d => d.Key).SelectMany(d => d.Value.Keys)
            .Concat(resilienceBehaviors.OrderBy(r => r.Key).SelectMany(r => r.Value.Keys))
            .Concat(handlers.OrderBy(h => h.Key).SelectMany(hdls => hdls.Value.Keys))
            .Distinct();

        return (allRegistrations, notifier);
    }

    private static void BuildResgistration(BuildResgistrationArguments arguments)
    {
        var (hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors, handlers, notifier, handlerType, handlerName, handlerKeyArgument) = arguments;

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

            ProcessHandlerInterface((
                name, arity, handlerName, handlerKeyArgument, args,
                hasDiagnostics, hasResilience,
                diagnosticsBehaviors, resilienceBehaviors, handlers));
        }
    }

    private static void ProcessHandlerInterface(ProcessHandlerInterfaceArguments arguments)
    {
        AddCommand(arguments);

        AddRequest(arguments);
    }

    private static void AddRequest(ProcessHandlerInterfaceArguments arguments)
    {
        var (interfaceName, arity, handlerName, handlerKeyArgument, args, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors, handlers) = arguments;

        if (arity == 2 && args.Length == 2)
        {
            if (!IsAccessible(args[0]) || !IsAccessible(args[1])) return;

            var msg = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var resp = args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            switch (interfaceName)
            {
                case "IRequestHandler":
                    AddRequestBehaviors(msg, resp, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors);
                    handlers.AddIfNew($"configure.RegisterRequestHandler<{handlerName}, {msg}, {resp}>({handlerKeyArgument});");
                    break;

                case "IStreamHandler":
                    AddStreamBehaviors(msg, resp, hasDiagnostics, diagnosticsBehaviors);
                    handlers.AddIfNew($"configure.RegisterStreamHandler<{handlerName}, {msg}, {resp}>({handlerKeyArgument});");
                    break;
            }
        }
    }

    private static void AddCommand(ProcessHandlerInterfaceArguments arguments)
    {
        var (interfaceName, arity, handlerName, handlerKeyArgument, args, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors, handlers) = arguments;

        if (arity == 1 && args.Length == 1)
        {
            if (!IsAccessible(args[0])) return;

            var msg = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            switch (interfaceName)
            {
                case "ICommandHandler":
                    AddCommandNotificationBehaviors(msg, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors);
                    handlers.AddIfNew($"configure.RegisterCommandHandler<{handlerName}, {msg}>({handlerKeyArgument});");
                    break;

                case "INotificationHandler":
                    AddCommandNotificationBehaviors(msg, hasDiagnostics, hasResilience, diagnosticsBehaviors, resilienceBehaviors);
                    handlers.AddIfNew($"configure.RegisterNotificationHandler<{handlerName}, {msg}>({handlerKeyArgument});");
                    break;
            }
        }
    }

    private static string BuildHandlerKeyArgument(INamedTypeSymbol handlerType)
    {
        var keyAttribute = handlerType
            .GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == KeyedServiceAttributeMetadataName);

        if (keyAttribute is null)
            return "null";

        var keyValue = GetNamedArgumentValue(keyAttribute, "Key")
            ?? GetConstructorArgumentValue(keyAttribute, 0);

        return TryFormatLiteral(keyValue, out var keyLiteral)
            ? keyLiteral
            : "null";
    }

    private static int GetOrderArgument(INamedTypeSymbol type)
    {
        var keyAttribute = type
            .GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == ServiceOrderAttributeMetadataName);

        if (keyAttribute is null)
            return int.MaxValue;

        var orderValue = GetConstructorArgumentValue(keyAttribute, 0);
        var order = TryExtractValue<int>(orderValue, out var ord) ? ord : int.MaxValue;

        return order;
    }

    private static TypedConstant? GetNamedArgumentValue(AttributeData attribute, string argumentName)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == argumentName)
                return argument.Value;
        }

        return null;
    }

    private static TypedConstant? GetConstructorArgumentValue(AttributeData attribute, int index)
    {
        if (index < 0 || index >= attribute.ConstructorArguments.Length)
            return null;

        return attribute.ConstructorArguments[index];
    }

    private static bool TryExtractValue<T>(TypedConstant? constant, out T extracted)
    {
        extracted = default!;

        if (constant is null || constant.Value.IsNull)
            return false;

        if (constant.Value.Kind is not TypedConstantKind.Primitive and not TypedConstantKind.Enum)
            return false;

        var value = constant.Value.Value;
        if (value is null)
            return false;

        switch (value)
        {
            case T t:
                extracted = t;
                return true;
        }

        return false;
    }

    private static bool TryFormatLiteral(TypedConstant? constant, out string literal)
    {
        literal = "null";

        if (constant is null || constant.Value.IsNull)
            return true;

        if (constant.Value.Kind is not TypedConstantKind.Primitive and not TypedConstantKind.Enum)
            return false;

        var value = constant.Value.Value;
        if (value is null)
            return true;

        switch (value)
        {
            case string text:
                literal = SymbolDisplay.FormatLiteral(text, true);
                return true;
            case char character:
                literal = SymbolDisplay.FormatLiteral(character, true);
                return true;
            case bool boolean:
                literal = boolean ? "true" : "false";
                return true;
        }

        if (constant.Value.Type?.TypeKind == TypeKind.Enum)
        {
            var enumTypeName = constant.Value.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            literal = $"({enumTypeName}){FormatPrimitiveLiteral(value)}";
            return true;
        }

        literal = FormatPrimitiveLiteral(value);
        return true;
    }

    private static string FormatPrimitiveLiteral(object value) => value switch
    {
        byte number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        sbyte number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        short number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ushort number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        uint number => $"{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}U",
        long number => $"{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}L",
        ulong number => $"{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}UL",
        float number => SymbolDisplay.FormatPrimitive(number, quoteStrings: true, useHexadecimalNumbers: false),
        double number => SymbolDisplay.FormatPrimitive(number, quoteStrings: true, useHexadecimalNumbers: false),
        decimal number => $"{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}M",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
    };

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

    private static string BuildSource(IEnumerable<string> registrations, StringBuilder notifier, string frameworkBehaviors, string assemblyName)
    {
        const string indent = "            ";
        var registrationsBlock = string.Join(
            "\n",
            registrations.Select(r => indent + r)
        );

        return LoadTemplate()
            .Replace(NotifierToken, notifier.ToString())
            .Replace(RegistrationsToken, registrationsBlock)
            .Replace(FrameworkBehaviorsToken, frameworkBehaviors)
            .Replace(AssemblyNamespaceToken, assemblyName);
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

    /// <summary>
    /// Inserts <paramref name="key"/> with a <c>true</c> value if the key is not already present.
    /// Provides insertion-ordered deduplication on <c>netstandard2.0</c> where
    /// <see cref="Dictionary{TKey,TValue}.TryAdd"/> is unavailable.
    /// </summary>
    internal static void AddIfNew(this Dictionary<int, Dictionary<string, bool>> dict, int key)
    {
        if (!dict.ContainsKey(key))
            dict[key] = [];
    }
}
