using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NetMediate.SourceGeneration;

namespace NetMediate.SourceGeneration.Tests;

/// <summary>
/// Integration tests that verify <see cref="NetMediateRegistrationGenerator"/> behaviour when
/// code is compiled against a pre-built <c>NetMediate.dll</c> — i.e. the same scenario a user
/// experiences when referencing the NuGet package instead of a project reference.
/// </summary>
public sealed class GeneratorIntegrationTests
{
    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the generator against an in-memory compilation built with the given source text and
    /// a reference to the real NetMediate.dll. Returns the generated source for
    /// <c>NetMediateGeneratedDI.g.cs</c>.
    /// </summary>
    private static (string generatedSource, ImmutableArray<Diagnostic> diagnostics) RunGenerator(
        string assemblyName,
        string userSource,
        bool includeNetMediateDll = true)
    {
        var references = BuildReferences(includeNetMediateDll);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(userSource)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NetMediateRegistrationGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out _, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        var generatedSource = runResult.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("NetMediateGeneratedDI.g.cs"))
            ?.GetText()
            .ToString() ?? string.Empty;

        return (generatedSource, generatorDiagnostics);
    }

    private static List<MetadataReference> BuildReferences(bool includeNetMediateDll)
    {
        var refs = new List<MetadataReference>
        {
            // Core .NET references needed for compilation
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IAsyncEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        };

        // Add all loaded assemblies to avoid type resolution failures
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
            {
                try { refs.Add(MetadataReference.CreateFromFile(asm.Location)); }
                catch { /* ignore unloadable assemblies */ }
            }
        }

        if (includeNetMediateDll)
        {
            // Reference the real NetMediate.dll — this is what package reference users get.
            refs.Add(MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location));
        }

        return refs;
    }

    // ── tests ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the generator runs on the <c>NetMediate</c> assembly itself (as happens during
    /// package build), it must NOT emit the <c>NetMediateGeneratedDI</c> class.  Emitting the
    /// class would bake it into <c>NetMediate.dll</c>, causing a duplicate-type compile error
    /// in any downstream project that references the package.
    /// </summary>
    [Fact]
    public void Generator_WhenBuildingNetMediateAssembly_ShouldSkipEmission()
    {
        // Use "NetMediate" as the assembly name — exactly what happens during package build.
        var (generatedSource, _) = RunGenerator(
            assemblyName: "NetMediate",
            userSource: "// empty project",
            includeNetMediateDll: false);

        // Must NOT emit the class — only a placeholder comment.
        Assert.DoesNotContain("class NetMediateGeneratedDI", generatedSource);
        Assert.DoesNotContain("public static", generatedSource);
        Assert.Contains("Source generation skipped", generatedSource);
    }

    /// <summary>
    /// When the generator runs on a <em>user</em> project that references the
    /// <c>NetMediate.dll</c> (package reference scenario), it should emit a full
    /// <c>AddNetMediate()</c> method with all discovered handlers registered.
    /// </summary>
    [Fact]
    public void Generator_WhenBuildingUserProject_ShouldEmitAddNetMediate()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record MyCommand(string Value);

            public sealed class MyCommandHandler : ICommandHandler<MyCommand>
            {
                public Task Handle(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, diagnostics) = RunGenerator(
            assemblyName: "MyApp",
            userSource: userSource);

        // Should have no generator errors
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // The generated class should be present
        Assert.Contains("class NetMediateGeneratedDI", generatedSource);
        Assert.Contains("AddNetMediate", generatedSource);
    }

    /// <summary>
    /// When a user project has a command handler, the generator should emit a
    /// <c>RegisterCommandHandler</c> call for it.
    /// </summary>
    [Fact]
    public void Generator_WhenUserProjectHasCommandHandler_ShouldRegisterIt()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PingCommand;

            public sealed class PingHandler : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("RegisterCommandHandler", generatedSource);
        Assert.Contains("PingHandler", generatedSource);
        Assert.Contains("PingCommand", generatedSource);
    }

    /// <summary>
    /// When a user project has a request handler, the generator should emit a
    /// <c>RegisterRequestHandler</c> call for it.
    /// </summary>
    [Fact]
    public void Generator_WhenUserProjectHasRequestHandler_ShouldRegisterIt()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record GetQuery(int Id);

            public sealed class GetHandler : IRequestHandler<GetQuery, string>
            {
                public Task<string> Handle(GetQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult(query.Id.ToString());
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("RegisterRequestHandler", generatedSource);
        Assert.Contains("GetHandler", generatedSource);
    }

    /// <summary>
    /// When a user project has a notification handler, the generator should emit a
    /// <c>RegisterNotificationHandler</c> call for it.
    /// </summary>
    [Fact]
    public void Generator_WhenUserProjectHasNotificationHandler_ShouldRegisterIt()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record AlertNotification(string Message);

            public sealed class AlertHandler : INotificationHandler<AlertNotification>
            {
                public Task Handle(AlertNotification notification, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("RegisterNotificationHandler", generatedSource);
        Assert.Contains("AlertHandler", generatedSource);
    }

    /// <summary>
    /// Validates that the generated <c>AddNetMediate()</c> compiles successfully when combined
    /// with the user's handler code — i.e. the emitted registrations reference valid types.
    /// </summary>
    [Fact]
    public void Generator_WhenUserProjectHasHandlers_GeneratedCodeShouldCompileCleanly()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record SampleCommand;

            public sealed class SampleHandler : ICommandHandler<SampleCommand>
            {
                public Task Handle(SampleCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var refs = BuildReferences(includeNetMediateDll: true);

        var compilation = CSharpCompilation.Create(
            "MyApp",
            syntaxTrees: [CSharpSyntaxTree.ParseText(userSource)],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NetMediateRegistrationGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        // The final compilation (original sources + generated code) should have no errors.
        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }
}
