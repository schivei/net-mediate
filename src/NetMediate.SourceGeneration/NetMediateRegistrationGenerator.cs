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
                var registrations = BuildRegistrations(types);
                var source = BuildSource(registrations);
                sourceProductionContext.AddSource(
                    "NetMediateGeneratedDI.g.cs",
                    source
                );
            }
        );
    }

    private static string[] BuildRegistrations(ImmutableArray<INamedTypeSymbol> types)
    {
        var registrations = new HashSet<string>(StringComparer.Ordinal);

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

                    registrations.Add(
                        $"builder.RegisterCommandHandler<{args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {handlerName}>();"
                    );
                    continue;
                }

                if (
                    definitionName == "INotificationHandler"
                    && definitionArity == 1
                    && args.Length == 1
                )
                {
                    if (!IsAccessible(args[0]))
                        continue;

                    registrations.Add(
                        $"builder.RegisterNotificationHandler<{args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {handlerName}>();"
                    );
                    continue;
                }

                if (definitionName == "IRequestHandler" && definitionArity == 2 && args.Length == 2)
                {
                    if (!IsAccessible(args[0]) || !IsAccessible(args[1]))
                        continue;

                    registrations.Add(
                        $"builder.RegisterRequestHandler<{args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {handlerName}>();"
                    );
                    continue;
                }

                if (definitionName == "IStreamHandler" && definitionArity == 2 && args.Length == 2)
                {
                    if (!IsAccessible(args[0]) || !IsAccessible(args[1]))
                        continue;

                    registrations.Add(
                        $"builder.RegisterStreamHandler<{args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {handlerName}>();"
                    );
                    continue;
                }

                if (definitionName == "IValidationHandler" && definitionArity == 1 && args.Length == 1)
                {
                    if (!IsAccessible(args[0]))
                        continue;

                    registrations.Add(
                        $"builder.RegisterValidationHandler<{args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {handlerName}>();"
                    );
                }
            }
        }

        return [.. registrations.OrderBy(r => r, StringComparer.Ordinal)];
    }

    private static string BuildSource(string[] registrations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("namespace NetMediate;");
        sb.AppendLine();
        sb.AppendLine("public static class NetMediateGeneratedDI");
        sb.AppendLine("{");
        sb.AppendLine("    public static IMediatorServiceBuilder AddNetMediateGenerated(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, bool excludeFromCodeCoverage = false)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (excludeFromCodeCoverage)");
        sb.AppendLine("        {");
        sb.AppendLine("            return AddNetMediateGeneratedExcludedFromCodeCoverage(services);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var builder = services.AddNetMediate(static _ => { });");
        sb.AppendLine("        RegisterGeneratedHandlers(builder);");
        sb.AppendLine("        return builder;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        sb.AppendLine("    private static IMediatorServiceBuilder AddNetMediateGeneratedExcludedFromCodeCoverage(Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        var builder = services.AddNetMediate(static _ => { });");
        sb.AppendLine("        RegisterGeneratedHandlers(builder);");
        sb.AppendLine("        return builder;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static void RegisterGeneratedHandlers(IMediatorServiceBuilder builder)");
        sb.AppendLine("    {");

        foreach (var registration in registrations)
            sb.AppendLine($"        {registration}");

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
