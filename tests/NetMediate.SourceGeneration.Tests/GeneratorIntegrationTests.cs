using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// Loads <c>NetMediateRegistrationGenerator</c> from the local source-generator build output
    /// when available, falling back to the analyzer DLL bundled inside the <c>NetMediate</c>
    /// package. The package layout is:
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
        var generatorDll = GetLocalGeneratorDllPath();

        if (!File.Exists(generatorDll))
        {
            var packageRoot = GetNetMediatePackageRoot();
            generatorDll = Path.Combine(
                packageRoot,
                "analyzers",
                "dotnet",
                "cs",
                "NetMediate.SourceGeneration.dll");
        }

        if (!File.Exists(generatorDll))
            throw new FileNotFoundException(
                $"NetMediate.SourceGeneration.dll not found at '{generatorDll}'. " +
                $"Ensure the referenced NetMediate package contains the bundled analyzer.",
                generatorDll);

        // Assembly.LoadFrom resolves Microsoft.CodeAnalysis from the already-loaded instance in
        // the default load context, so IIncrementalGenerator identity is preserved.
        var asm = Assembly.LoadFrom(generatorDll);
        var type = asm.GetType("NetMediate.SourceGeneration.NetMediateRegistrationGenerator")
            ?? throw new InvalidOperationException(
                   "NetMediateRegistrationGenerator type not found in the loaded assembly.");

        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    private static string GetLocalGeneratorDllPath()
    {
        var configuration = GetBuildConfiguration();
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "NetMediate.SourceGeneration", "bin", configuration, "netstandard2.0", "NetMediate.SourceGeneration.dll"));
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string GetNetMediatePackageRoot()
    {
        var assetsFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "project.assets.json"));

        if (!File.Exists(assetsFile))
            throw new FileNotFoundException($"Restore assets file not found at '{assetsFile}'.", assetsFile);

        using var stream = File.OpenRead(assetsFile);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            throw new InvalidOperationException("The restore assets file does not contain a libraries section.");

        var packagePath = libraries.EnumerateObject()
            .Select(static library => library.Name)
            .FirstOrDefault(static name => name.StartsWith("NetMediate/", StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException("The restore assets file does not contain the NetMediate package entry.");

        var nugetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        var packageVersion = packagePath[(packagePath.IndexOf('/') + 1)..];
        return Path.Combine(nugetPackages, "netmediate", packageVersion);
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
                typeof(IServiceCollection).Assembly.Location),
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

    [Fact]
    public void Generator_WhenCommandHandlerHasKeyedServiceAttribute_ShouldRegisterWithKey()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PingCommand;

            [KeyedService(Key = "primary")]
            public sealed class PingHandler : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("primary", generatedSource);
        Assert.Contains("RegisterCommandHandler<global::MyApp.PingHandler, global::MyApp.PingCommand>(\"primary\")", generatedSource);
    }

    [Fact]
    public void Generator_WhenRequestHandlerHasKeyedServiceAttribute_ShouldRegisterWithKey()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record GetQuery(int Id);

            [KeyedService(Key = 42)]
            public sealed class GetHandler : IRequestHandler<GetQuery, string>
            {
                public Task<string> Handle(GetQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult(query.Id.ToString());
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("42", generatedSource);
        Assert.Contains("RegisterRequestHandler<global::MyApp.GetHandler, global::MyApp.GetQuery, string>(42)", generatedSource);
    }

    [Fact]
    public void Generator_WhenNotificationHandlerHasKeyedServiceAttribute_ShouldRegisterWithKey()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record AlertNotification(string Message);

            [KeyedService(Key = true)]
            public sealed class AlertHandler : INotificationHandler<AlertNotification>
            {
                public Task Handle(AlertNotification notification, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("true", generatedSource);
        Assert.Contains("RegisterNotificationHandler<global::MyApp.AlertHandler, global::MyApp.AlertNotification>(true)", generatedSource);
    }

    [Fact]
    public void Generator_WhenStreamHandlerHasKeyedServiceAttribute_ShouldRegisterWithKey()
    {
        const string userSource = """
            using NetMediate;
            using System.Collections.Generic;
            using System.Threading;

            namespace MyApp;

            public sealed record StreamQuery;

            [KeyedService(Key = 'a')]
            public sealed class StreamHandler : IStreamHandler<StreamQuery, int>
            {
                public async IAsyncEnumerable<int> Handle(StreamQuery query, CancellationToken cancellationToken = default)
                {
                    yield return 1;
                    await Task.CompletedTask;
                }
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("'a'", generatedSource);
        Assert.Contains("RegisterStreamHandler<global::MyApp.StreamHandler, global::MyApp.StreamQuery, int>('a')", generatedSource);
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

    // local generation
    public record MyCommand(int Key) : IRequest<int>;

    public sealed class MyCommandHandler : IRequestHandler<MyCommand, int>
    {
        public Task<int> Handle(MyCommand message, CancellationToken cancellationToken = default) =>
            Task.FromResult(message.Key);
    }

    [KeyedService(Key = "secondary")]
    public sealed class AnotherCommandHandler : IRequestHandler<MyCommand, int>
    {
        public Task<int> Handle(MyCommand message, CancellationToken cancellationToken = default) =>
            Task.FromResult(message.Key);
    }

    [Fact]
    public async Task Generator_LocalInstance()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddNetMediate();
        var loggerFactory = LoggerFactory.Create(_ => { });
        serviceCollection.AddSingleton(loggerFactory);
        serviceCollection.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        serviceCollection.AddSingleton<ILogger, Logger<GeneratorIntegrationTests>>();

        var service = serviceCollection.BuildServiceProvider();

        // Handlers without [KeyedService] are registered under DEFAULT_ROUTING_KEY ("__default"),
        // not as unkeyed services, so GetKeyedService must be used here.
        var hasHandler = service.GetKeyedService<IRequestHandler<MyCommand, int>>(NetMediateDI.DEFAULT_ROUTING_KEY);
        Assert.IsType<MyCommandHandler>(hasHandler);

        var mediator = service.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);

        var result = await mediator.Request(new MyCommand(1));
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Generator_SecondaryHandler()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddNetMediate();
        var loggerFactory = LoggerFactory.Create(_ => { });
        serviceCollection.AddSingleton(loggerFactory);
        serviceCollection.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        serviceCollection.AddSingleton<ILogger, Logger<GeneratorIntegrationTests>>();
        var service = serviceCollection.BuildServiceProvider();
        var hasHandler = service.GetKeyedService<IRequestHandler<MyCommand, int>>("secondary");
        Assert.IsType<AnotherCommandHandler>(hasHandler);
        var mediator = service.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);
        var result = await mediator.Request("secondary", new MyCommand(2), TestContext.Current.CancellationToken);
        Assert.Equal(2, result);
    }
}
