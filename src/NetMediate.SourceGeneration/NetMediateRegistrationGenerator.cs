using System.IO;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetMediate.SourceGeneration;

[Generator]
public sealed class NetMediateRegistrationGenerator : IIncrementalGenerator
{
    // Placeholder tokens that are substituted into the embedded code template.
    private const string NotifierToken = "{{Notifier}}";
    private const string RegistrationsToken = "{{Registrations}}";

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

        context.RegisterSourceOutput(
            handlerTypes,
            static (sourceProductionContext, types) =>
            {
                var (registrations, notifier) = BuildRegistrations(types);
                var source = BuildSource(registrations, notifier);
                sourceProductionContext.AddSource(
                    "NetMediateGeneratedDI.g.cs",
                    source
                );
            }
        );
    }

    private static (IEnumerable<string> registrations, string notifier) BuildRegistrations(
        ImmutableArray<INamedTypeSymbol> types)
    {
        var registrations = new HashSet<string>(StringComparer.Ordinal);
        var notifier = "AddNetMediate";

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

                var registration = TryBuildHandlerRegistration(name, arity, handlerName, args);
                if (registration is not null)
                    registrations.Add(registration);
            }
        }

        return (registrations, notifier);
    }

    /// <summary>
    /// Builds a single handler registration line for 1-arg (command/notification) or
    /// 2-arg (request/stream) handler interfaces; returns <see langword="null"/> when the
    /// interface is not a recognised handler or any type argument is inaccessible.
    /// </summary>
    private static string? TryBuildHandlerRegistration(
        string interfaceName,
        int arity,
        string handlerName,
        ImmutableArray<ITypeSymbol> args)
    {
        if (arity == 1 && args.Length == 1)
        {
            if (!IsAccessible(args[0]))
                return null;

            var msgType = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return interfaceName switch
            {
                "ICommandHandler" =>
                    $"configure.RegisterCommandHandler<{handlerName}, {msgType}>();",
                "INotificationHandler" =>
                    $"configure.RegisterNotificationHandler<{handlerName}, {msgType}>();",
                _ => null,
            };
        }

        if (arity == 2 && args.Length == 2)
        {
            if (!IsAccessible(args[0]) || !IsAccessible(args[1]))
                return null;

            var msgType  = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var respType = args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return interfaceName switch
            {
                "IRequestHandler" =>
                    $"configure.RegisterRequestHandler<{handlerName}, {msgType}, {respType}>();",
                "IStreamHandler" =>
                    $"configure.RegisterStreamHandler<{handlerName}, {msgType}, {respType}>();",
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Renders the embedded code template, substituting the notifier expression and the
    /// collected registration lines for their respective placeholder tokens.
    /// </summary>
    private static string BuildSource(IEnumerable<string> registrations, string notifier)
    {
        const string indent = "            "; // 12 spaces – matches the template indentation
        var registrationsBlock = string.Join(
            "\n",
            registrations.Select(r => indent + r)
        );

        return LoadTemplate()
            .Replace(NotifierToken, notifier)
            .Replace(RegistrationsToken, registrationsBlock);
    }

    /// <summary>
    /// Reads the embedded template file and returns its content as a string.
    /// Throws <see cref="InvalidOperationException"/> if the resource is missing,
    /// which would only happen if the project was misconfigured (template file removed).
    /// </summary>
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

