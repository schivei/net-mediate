using System.Text;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetMediate.SourceGeneration;

[Generator]
public sealed class NetMediateRegistrationGenerator : IIncrementalGenerator
{
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

    private static (IEnumerable<string> registrations, string notifier) BuildRegistrations(ImmutableArray<INamedTypeSymbol> types)
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

                var definitionName = definition.Name;
                var definitionArity = definition.Arity;
                var args = @interface.TypeArguments;

                if (definitionName == "ICommandHandler" && definitionArity == 1 && args.Length == 1)
                {
                    if (!IsAccessible(args[0]))
                        continue;

                    var msgType = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var ifaceType = @interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    registrations.Add(
                        $"configure.RegisterHandler<{ifaceType}, {handlerName}, {msgType}, global::System.Threading.Tasks.Task>();"
                    );
                    continue;
                }

                if (definitionName == "INotificationHandler" && definitionArity == 1 && args.Length == 1)
                {
                    if (!IsAccessible(args[0]))
                        continue;

                    var msgType = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var ifaceType = @interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    registrations.Add(
                        $"configure.RegisterHandler<{ifaceType}, {handlerName}, {msgType}, global::System.Threading.Tasks.Task>();"
                    );
                    continue;
                }

                if (definitionName == "IRequestHandler" && definitionArity == 2 && args.Length == 2)
                {
                    if (!IsAccessible(args[0]) || !IsAccessible(args[1]))
                        continue;

                    var msgType = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var respType = args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var ifaceType = @interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    registrations.Add(
                        $"configure.RegisterHandler<{ifaceType}, {handlerName}, {msgType}, global::System.Threading.Tasks.Task<{respType}>>();"
                    );
                    continue;
                }

                if (definitionName == "IStreamHandler" && definitionArity == 2 && args.Length == 2)
                {
                    if (!IsAccessible(args[0]) || !IsAccessible(args[1]))
                        continue;

                    var msgType = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var respType = args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var ifaceType = @interface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    registrations.Add(
                        $"configure.RegisterHandler<{ifaceType}, {handlerName}, {msgType}, global::System.Collections.Generic.IAsyncEnumerable<{respType}>>();"
                    );
                    continue;
                }

                if (definitionName == "INotifier" && definitionArity == 0 && args.Length == 0)
                {
                    notifier += $"<{handlerName}>";
                }
            }
        }

        return (registrations, notifier);
    }

    private static string BuildSource(IEnumerable<string> registrations, string notifier)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("namespace NetMediate;");
        sb.AppendLine();
        sb.AppendLine("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        sb.AppendLine("public static class NetMediateGeneratedDI");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::NetMediate.IMediatorServiceBuilder AddNetMediateGenerated(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return services.{notifier}(configure =>");
        sb.AppendLine("        {");

        foreach (var registration in registrations)
            sb.AppendLine($"            {registration}");

        sb.AppendLine("        });");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
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
