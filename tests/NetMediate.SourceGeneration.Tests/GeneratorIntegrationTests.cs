using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetMediate.SourceGeneration.Tests;

/// <summary>
/// Integration tests that verify <c>NetMediateRegistrationGenerator</c> behaviour when code is
/// compiled against the <c>NetMediate</c> NuGet package — the exact scenario a user experiences
/// when running <c>dotnet add package NetMediate</c>.
///
/// The source generator (<c>NetMediate.SourceGeneration.dll</c>) is bundled inside the
/// <c>NetMediate</c> package as an analyzer.  At build time it runs on this test project itself;
/// at test-runtime the Roslyn API tests load it dynamically from the NuGet package cache.
/// </summary>
public sealed class GeneratorIntegrationTests
{
    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads <c>NetMediateRegistrationGenerator</c> from the analyzer DLL that is bundled inside
    /// the <c>NetMediate</c> NuGet package.  The package layout is:
    /// <code>
    ///   lib/{tfm}/NetMediate.dll                           ← runtime reference
    ///   analyzers/dotnet/cs/NetMediate.SourceGeneration.dll ← source generator
    /// </code>
    /// We locate the generator DLL by navigating to the NuGet global packages cache.
    /// NuGet lowercases package IDs in the cache on all platforms.
    /// <para>
    /// <c>Assembly.LoadFrom</c> resolves <c>Microsoft.CodeAnalysis</c> from the instance already
    /// loaded in the default context (loaded by this project's own package reference), so the
    /// <see cref="IIncrementalGenerator"/> type identity is preserved and the cast succeeds.
    /// </para>
    /// </summary>
    private static IIncrementalGenerator CreateGenerator()
    {
        // NuGet installs to the global packages cache. NUGET_PACKAGES can override the default.
        var nugetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                   Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                   ".nuget", "packages");

        // Package IDs are stored lowercase in the cache; version matches the PackageReference.
        var generatorDll = Path.Combine(
            nugetPackages, "netmediate", "0.0.1-internal",
            "analyzers", "dotnet", "cs", "NetMediate.SourceGeneration.dll");

        if (!File.Exists(generatorDll))
            throw new FileNotFoundException(
                $"NetMediate.SourceGeneration.dll not found at '{generatorDll}'. " +
                $"Run the pre-restore pack step first: " +
                $"dotnet build src/NetMediate/NetMediate.csproj -c Release /p:Version=0.0.1-internal && " +
                $"dotnet pack src/NetMediate/NetMediate.csproj -c Release --no-build /p:Version=0.0.1-internal --output ./local-packages",
                generatorDll);

        // Assembly.LoadFrom resolves Microsoft.CodeAnalysis from the already-loaded instance in
        // the default load context, so IIncrementalGenerator identity is preserved.
        var asm  = Assembly.LoadFrom(generatorDll);
        var type = asm.GetType("NetMediate.SourceGeneration.NetMediateRegistrationGenerator")
            ?? throw new InvalidOperationException(
                   "NetMediateRegistrationGenerator type not found in the loaded assembly.");

        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// Runs the generator against an in-memory compilation built with the given source text and
    /// (optionally) a reference to the real <c>NetMediate.dll</c>.  Returns the generated source
    /// for <c>NetMediateGeneratedDI.g.cs</c>.
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

        var generator = CreateGenerator();
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
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IAsyncEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
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
            refs.Add(MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location));

        return refs;
    }

    // ── tests ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Proves that the source generator ran on THIS test project at build time by verifying that
    /// <c>NetMediateGeneratedDI</c> was generated and compiled into this assembly.  The generator
    /// reaches this project via the <c>NetMediate</c> NuGet package reference (the analyzer DLL
    /// is bundled inside the package).  If the package was misconfigured or the generator had
    /// produced a duplicate-type error, this project would not have compiled.
    /// </summary>
    [Fact]
    public void TestProject_ReferencesNetMediatePackage_GeneratorRanOnBuildAndClassExists()
    {
        // The generated class lives in namespace NetMediate inside the test assembly.
        var generatedType = Assembly.GetExecutingAssembly()
            .GetType("NetMediate.NetMediateGeneratedDI");

        Assert.NotNull(generatedType);
    }

    /// <summary>
    /// When the generator runs on the <c>NetMediate</c> assembly itself (as happens during
    /// package build), it must NOT emit the <c>NetMediateGeneratedDI</c> class.  Emitting the
    /// class would bake it into <c>NetMediate.dll</c>, causing a duplicate-type compile error
    /// in any downstream project that references the package.
    /// </summary>
    [Fact]
    public void Generator_WhenBuildingNetMediateAssembly_ShouldSkipEmission()
    {
        var (generatedSource, _) = RunGenerator(
            assemblyName: "NetMediate",
            userSource: "// empty project",
            includeNetMediateDll: false);

        Assert.DoesNotContain("class NetMediateGeneratedDI", generatedSource);
        Assert.DoesNotContain("public static", generatedSource);
        Assert.Contains("Source generation skipped", generatedSource);
    }

    /// <summary>
    /// When the generator runs on a user project that references the <c>NetMediate.dll</c>
    /// (package reference scenario), it should emit a full <c>AddNetMediate()</c> method with
    /// all discovered handlers registered.
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

        var (generatedSource, diagnostics) = RunGenerator(assemblyName: "MyApp", userSource: userSource);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
        Assert.Contains("class NetMediateGeneratedDI", generatedSource);
        Assert.Contains("AddNetMediate", generatedSource);
    }

    /// <summary>Command handler registration is emitted for a user project.</summary>
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

    /// <summary>Request handler registration is emitted for a user project.</summary>
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

    /// <summary>Notification handler registration is emitted for a user project.</summary>
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
    /// with the user's handler code.
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

        var generator = CreateGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }
}

