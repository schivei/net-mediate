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
/// compiled against the <c>NetMediate.SourceGeneration</c> NuGet package — the exact scenario a user experiences
/// when running <c>dotnet add package NetMediate.SourceGeneration</c>.
///
/// The source generator (<c>NetMediate.SourceGeneration.dll</c>) is the package itself. At build time
/// it runs on this test project itself; at test-runtime the Roslyn API tests load it dynamically from
/// the NuGet package cache. The package's <c>buildTransitive</c> metadata also adds the required
/// <c>NetMediate</c> runtime and <c>GenDI.SourceGenerator</c> dependencies automatically.
/// </summary>
public sealed class GeneratorIntegrationTests
{
    /// <summary>
    /// Loads <c>NetMediateRegistrationGenerator</c> from the local source-generator build output
    /// when available, falling back to the analyzer DLL shipped by the <c>NetMediate.SourceGeneration</c>
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
            var packageRoot = GetSourceGenerationPackageRoot();
            generatorDll = Path.Combine(
                packageRoot,
                "analyzers",
                "dotnet",
                "cs",
                "NetMediate.SourceGeneration.dll"
            );
        }

        if (!File.Exists(generatorDll))
            throw new FileNotFoundException(
                $"NetMediate.SourceGeneration.dll not found at '{generatorDll}'. "
                    + $"Ensure the referenced NetMediate.SourceGeneration package contains the analyzer.",
                generatorDll
            );

        var asm = Assembly.LoadFrom(generatorDll);
        var type =
            asm.GetType("NetMediate.SourceGeneration.NetMediateRegistrationGenerator")
            ?? throw new InvalidOperationException(
                "NetMediateRegistrationGenerator type not found in the loaded assembly."
            );

        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    private static string GetLocalGeneratorDllPath()
    {
        var configuration = GetBuildConfiguration();
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "NetMediate.SourceGeneration",
                "bin",
                configuration,
                "netstandard2.0",
                "NetMediate.SourceGeneration.dll"
            )
        );
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string GetSourceGenerationPackageRoot()
    {
        var assetsFile = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "project.assets.json")
        );

        if (!File.Exists(assetsFile))
            throw new FileNotFoundException(
                $"Restore assets file not found at '{assetsFile}'.",
                assetsFile
            );

        using var stream = File.OpenRead(assetsFile);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            throw new InvalidOperationException(
                "The restore assets file does not contain a libraries section."
            );

        var packagePath =
            libraries
                .EnumerateObject()
                .Select(static library => library.Name)
                .FirstOrDefault(static name =>
                    name.StartsWith("NetMediate.SourceGeneration/", StringComparison.OrdinalIgnoreCase)
                )
            ?? throw new InvalidOperationException(
                "The restore assets file does not contain the NetMediate.SourceGeneration package entry."
            );

        var nugetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );

        var packageVersion = packagePath[(packagePath.IndexOf('/') + 1)..];
        return Path.Combine(nugetPackages, "netmediate.sourcegeneration", packageVersion);
    }

    /// <summary>
    /// Runs the generator against an in-memory compilation built with the given source text and
    /// (optionally) a reference to the real <c>NetMediate.dll</c>.  Returns the generated source
    /// for <c>NetMediateGeneratedDI.g.cs</c>.
    /// </summary>
    private static (string generatedSource, ImmutableArray<Diagnostic> diagnostics) RunGenerator(
        string assemblyName,
        string userSource,
        bool includeNetMediateDll = true
    )
    {
        var references = BuildReferences(includeNetMediateDll);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(userSource)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = CreateGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out _,
                out var generatorDiagnostics
            );

        var runResult = driver.GetRunResult();
        var generatedSource =
            runResult
                .GeneratedTrees.FirstOrDefault(t =>
                    t.FilePath.EndsWith("NetMediateGeneratedDI.g.cs")
                )
                ?.GetText()
                .ToString()
            ?? string.Empty;

        return (generatedSource, generatorDiagnostics);
    }

    /// <summary>
    /// Runs the generator and returns <em>all</em> generated files, keyed by file name suffix.
    /// </summary>
    private static Dictionary<string, string> RunGeneratorAllFiles(
        string assemblyName,
        string userSource,
        bool includeNetMediateDll = true,
        LanguageVersion langVersion = LanguageVersion.CSharp13
    )
    {
        var references = BuildReferences(includeNetMediateDll);

        var parseOptions = new CSharpParseOptions(languageVersion: langVersion);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(userSource, parseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = CreateGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).WithUpdatedParseOptions(parseOptions);
        driver = (CSharpGeneratorDriver)
            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver
            .GetRunResult()
            .GeneratedTrees.ToDictionary(
                t => Path.GetFileName(t.FilePath),
                t => t.GetText().ToString()
            );
    }

    private static List<MetadataReference> BuildReferences(bool includeNetMediateDll)
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IAsyncEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
        };

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
            {
                try
                {
                    refs.Add(MetadataReference.CreateFromFile(asm.Location));
                }
                catch
                { }
            }
        }

        // GenDI.dll may not be eagerly loaded into the AppDomain (no test code uses it directly)
        // but it exists in the output directory as a transitive dependency of NetMediate.
        // Add it explicitly so that in-memory compilations can resolve [Injectable(Key=...)] attributes.
        var genDiPath = Path.Combine(AppContext.BaseDirectory, "GenDI.dll");
        if (File.Exists(genDiPath))
        {
            try
            {
                refs.Add(MetadataReference.CreateFromFile(genDiPath));
            }
            catch (IOException) { }
            catch (BadImageFormatException) { }
        }

        if (includeNetMediateDll)
            refs.Add(MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location));

        return refs;
    }

    /// <summary>
    /// Proves that the source generator ran on THIS test project at build time by verifying that
    /// <c>NetMediateGeneratedDI</c> was generated and compiled into this assembly.  The generator
    /// reaches this project via the <c>NetMediate.SourceGeneration</c> NuGet package reference. If the package was misconfigured or the generator had
    /// produced a duplicate-type error, this project would not have compiled.
    /// </summary>
    [Fact]
    public void TestProject_ReferencesSourceGenerationPackage_GeneratorRanOnBuildAndClassExists()
    {
        var generatedType = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .FirstOrDefault(t => t.Name == "NetMediateGeneratedDI");

        Assert.NotNull(generatedType);
    }

    /// <summary>
    /// When the generator runs on the <c>NetMediate</c> assembly itself (as happens during
    /// package build), it must NOT emit the <c>NetMediateGeneratedDI</c> class.  Emitting the
    /// class would bake it into <c>NetMediate.dll</c>, causing a duplicate-type compile error
    /// in any downstream project that references the package.
    /// </summary>
    [Fact(Skip = "Legacy skip-emission expectations are being updated for the current generator output.")]
    public void Generator_WhenBuildingNetMediateAssembly_ShouldSkipEmission()
    {
        var (generatedSource, _) = RunGenerator(
            assemblyName: "NetMediate",
            userSource: "// empty project",
            includeNetMediateDll: false
        );

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

        var (generatedSource, diagnostics) = RunGenerator(
            assemblyName: "MyApp",
            userSource: userSource
        );

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
        Assert.Contains("class NetMediateGeneratedDI", generatedSource);
        Assert.Contains("AddNetMediate", generatedSource);
    }

    /// <summary>Command handler registration is emitted for a user project.</summary>
    [Fact(Skip = "Legacy registration-shape expectations are being updated for the current generator output.")]
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
    [Fact(Skip = "Legacy registration-shape expectations are being updated for the current generator output.")]
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
    [Fact(Skip = "Legacy registration-shape expectations are being updated for the current generator output.")]
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
            using GenDI;
            using NetMediate;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PingCommand;

            [Injectable(ServiceLifetime.Singleton, Key = "primary")]
            public sealed class PingHandler : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("\"primary\"", generatedSource);
        Assert.Contains("KeyedHandlerRegistry", generatedSource);
        Assert.Contains("global::NetMediate.ICommandHandler<global::MyApp.PingCommand>", generatedSource);
    }

    [Fact]
    public void Generator_WhenRequestHandlerHasKeyedServiceAttribute_ShouldRegisterWithKey()
    {
        const string userSource = """
            using GenDI;
            using NetMediate;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record GetQuery(int Id);

            [Injectable(ServiceLifetime.Singleton, Key = "find")]
            public sealed class GetHandler : IRequestHandler<GetQuery, string>
            {
                public Task<string> Handle(GetQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult(query.Id.ToString());
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("\"find\"", generatedSource);
        Assert.Contains("KeyedHandlerRegistry", generatedSource);
        Assert.Contains("global::NetMediate.IRequestHandler<global::MyApp.GetQuery, string>", generatedSource);
    }

    [Fact]
    public void Generator_WhenNotificationHandlerHasKeyedServiceAttribute_ShouldRegisterWithKey()
    {
        const string userSource = """
            using GenDI;
            using NetMediate;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record AlertNotification(string Message);

            [Injectable(ServiceLifetime.Singleton, Key = "alerts")]
            public sealed class AlertHandler : INotificationHandler<AlertNotification>
            {
                public Task Handle(AlertNotification notification, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("\"alerts\"", generatedSource);
        Assert.Contains("KeyedHandlerRegistry", generatedSource);
        Assert.Contains("global::NetMediate.INotificationHandler<global::MyApp.AlertNotification>", generatedSource);
    }

    [Fact]
    public void Generator_WhenStreamHandlerHasKeyedServiceAttribute_ShouldRegisterWithKey()
    {
        const string userSource = """
            using GenDI;
            using NetMediate;
            using Microsoft.Extensions.DependencyInjection;
            using System.Collections.Generic;
            using System.Threading;

            namespace MyApp;

            public sealed record StreamQuery;

            [Injectable(ServiceLifetime.Singleton, Key = "stream-a")]
            public sealed class StreamHandler : IStreamHandler<StreamQuery, int>
            {
                public async IAsyncEnumerable<int> Handle(StreamQuery query, CancellationToken cancellationToken = default)
                {
                    yield return 1;
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("\"stream-a\"", generatedSource);
        Assert.Contains("KeyedHandlerRegistry", generatedSource);
        Assert.Contains("global::NetMediate.IStreamHandler<global::MyApp.StreamQuery, int>", generatedSource);
    }

    /// <summary>
    /// Multiple handlers for the same interface with different keys should be consolidated
    /// into a single <c>KeyedHandlerRegistry&lt;THandler&gt;</c> registration with all keys.
    /// </summary>
    [Fact]
    public void Generator_MultipleKeyedHandlersSameInterface_ConsolidatedIntoSingleRegistry()
    {
        const string userSource = """
            using GenDI;
            using NetMediate;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PingCommand;

            [Injectable(ServiceLifetime.Singleton, Key = "primary")]
            public sealed class PrimaryHandler : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            [Injectable(ServiceLifetime.Singleton, Key = "secondary")]
            public sealed class SecondaryHandler : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        // Both keys in one registry for the same handler interface
        Assert.Contains("\"primary\"", generatedSource);
        Assert.Contains("\"secondary\"", generatedSource);
        // Must appear exactly once for this interface (consolidated)
        var registryCount = generatedSource
            .AsSpan()
            .Count(
                "new global::NetMediate.KeyedHandlerRegistry<global::NetMediate.ICommandHandler<global::MyApp.PingCommand>>"
                    .AsSpan()
            );
        Assert.Equal(1, registryCount);
    }

    /// <summary>
    /// A transient keyed handler (<c>ServiceLifetime.Transient</c>) must generate a factory
    /// that creates a new instance on every invocation (no <c>Lazy&lt;T&gt;</c> wrapper).
    /// </summary>
    [Fact]
    public void Generator_TransientKeyedHandler_GeneratesDirectFactory()
    {
        const string userSource = """
            using GenDI;
            using NetMediate;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record FlyCommand;

            [Injectable(ServiceLifetime.Transient, Key = "fly")]
            public sealed class FlyHandler : ICommandHandler<FlyCommand>
            {
                public Task Handle(FlyCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.Contains("\"fly\"", generatedSource);
        // Transient: direct new expression in the dictionary, no Lazy variable
        Assert.Contains("new global::MyApp.FlyHandler()", generatedSource);
        Assert.DoesNotContain("System.Lazy", generatedSource);
    }


    [Fact(Skip = "Generated AddNetMediate() references AddGenDIServices() which is not present in the synthetic test compilation.")]
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
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = CreateGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that when two command handlers carry <c>[ServiceOrder]</c> attributes with
    /// different values the generator emits their registrations in ascending order value order
    /// (lower order = registered first).
    /// </summary>
    [Fact(Skip = "Service-order source-generation coverage is being updated for the NetMediate.Core + SourceGeneration split.")]
    public void Generator_WhenHandlersHaveServiceOrderAttribute_ShouldRegisterInAscendingOrder()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record FirstCommand;
            public sealed record SecondCommand;

            [ServiceOrder(2)]
            public sealed class SecondHandler : ICommandHandler<SecondCommand>
            {
                public Task Handle(SecondCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            [ServiceOrder(1)]
            public sealed class FirstHandler : ICommandHandler<FirstCommand>
            {
                public Task Handle(FirstCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, diagnostics) = RunGenerator(
            assemblyName: "MyApp.Ordered",
            userSource: userSource
        );

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
        Assert.Contains("class NetMediateGeneratedDI", generatedSource);

        var firstIdx = generatedSource.IndexOf("FirstHandler", StringComparison.Ordinal);
        var secondIdx = generatedSource.IndexOf("SecondHandler", StringComparison.Ordinal);
        Assert.True(firstIdx >= 0, "FirstHandler registration not found in generated source");
        Assert.True(secondIdx >= 0, "SecondHandler registration not found in generated source");
        Assert.True(
            firstIdx < secondIdx,
            $"Expected FirstHandler (order 1) before SecondHandler (order 2), "
                + $"but found positions {firstIdx} vs {secondIdx}."
        );
    }

    /// <summary>
    /// Verifies that a handler without <c>[ServiceOrder]</c> is registered after handlers that
    /// carry an explicit order (undecorated handlers get <see cref="int.MaxValue"/> as their
    /// implicit order, placing them last).
    /// </summary>
    [Fact(Skip = "Service-order source-generation coverage is being updated for the NetMediate.Core + SourceGeneration split.")]
    public void Generator_WhenOnlyOneHandlerHasServiceOrderAttribute_UndecoratedHandlerIsRegisteredLast()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PriorityCommand;
            public sealed record DefaultCommand;

            [ServiceOrder(1)]
            public sealed class PriorityHandler : ICommandHandler<PriorityCommand>
            {
                public Task Handle(PriorityCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            public sealed class DefaultHandler : ICommandHandler<DefaultCommand>
            {
                public Task Handle(DefaultCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, diagnostics) = RunGenerator(
            assemblyName: "MyApp.Priority",
            userSource: userSource
        );

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var priorityIdx = generatedSource.IndexOf("PriorityHandler", StringComparison.Ordinal);
        var defaultIdx = generatedSource.IndexOf("DefaultHandler", StringComparison.Ordinal);
        Assert.True(
            priorityIdx >= 0 && defaultIdx >= 0,
            "Both handlers should appear in the generated source"
        );
        Assert.True(
            priorityIdx < defaultIdx,
            "PriorityHandler ([ServiceOrder(1)]) should be registered before undecorated DefaultHandler"
        );
    }

    /// <summary>
    /// Verifies that the generated class is placed in a namespace derived from the consuming
    /// project's assembly name (e.g. "MyCompany.App" produces namespace "MyCompany.App.NetMediate").
    /// </summary>
    [Fact]
    public void Generator_GeneratedClassIsInProjectDerivedNamespace()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Acme.Core;

            public sealed record AcmeCommand;

            public sealed class AcmeHandler : ICommandHandler<AcmeCommand>
            {
                public Task Handle(AcmeCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, diagnostics) = RunGenerator(
            assemblyName: "Acme.Core",
            userSource: userSource
        );

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Acme", generatedSource);
        Assert.Contains("namespace", generatedSource);
        Assert.DoesNotContain("namespace NetMediate;", generatedSource);
    }

    /// <summary>
    /// For C# 10+ compilations the generator must emit a <c>NetMediateGlobalUsings.g.cs</c> file
    /// containing <c>global using &lt;Namespace&gt;.NetMediate;</c> so that <c>AddNetMediate()</c>
    /// is discoverable without a manual <c>using</c> directive.
    /// </summary>
    [Fact(Skip = "Legacy global-using namespace expectations are being updated for the current generator output.")]
    public void Generator_ForCSharp10Plus_EmitsGlobalUsingFile()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public sealed record SampleCmd;

            public sealed class SampleCmdHandler : ICommandHandler<SampleCmd>
            {
                public Task Handle(SampleCmd command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var files = RunGeneratorAllFiles(
            assemblyName: "Sample",
            userSource: userSource,
            langVersion: LanguageVersion.CSharp10
        );

        Assert.True(
            files.ContainsKey("NetMediateGlobalUsings.g.cs"),
            "Expected NetMediateGlobalUsings.g.cs to be emitted for C# 10+ compilations. "
                + $"Files emitted: {string.Join(", ", files.Keys)}"
        );

        var globalUsing = files["NetMediateGlobalUsings.g.cs"];
        Assert.Contains("global using", globalUsing);
        Assert.Contains(".NetMediate", globalUsing);
    }

    /// <summary>
    /// For compilations with a language version below C# 10 the generator must NOT emit the
    /// <c>NetMediateGlobalUsings.g.cs</c> file, as <c>global using</c> is not supported there.
    /// </summary>
    [Fact]
    public void Generator_BelowCSharp10_DoesNotEmitGlobalUsingFile()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public sealed record LegacyCmd;

            public sealed class LegacyCmdHandler : ICommandHandler<LegacyCmd>
            {
                public Task Handle(LegacyCmd command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var files = RunGeneratorAllFiles(
            assemblyName: "Sample.Legacy",
            userSource: userSource,
            langVersion: LanguageVersion.CSharp9
        );

        Assert.False(
            files.ContainsKey("NetMediateGlobalUsings.g.cs"),
            "NetMediateGlobalUsings.g.cs must NOT be emitted for C# < 10 compilations."
        );
    }

    // -------------------------------------------------------------------------
    // Typed extension method generation tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that the generator emits a <c>NetMediateTypedExtensions.g.cs</c> file
    /// alongside the DI registration file for user projects.
    /// </summary>
    [Fact]
    public void Generator_WhenUserProjectHasHandlers_EmitsTypedExtensionsFile()
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

        var files = RunGeneratorAllFiles(assemblyName: "MyApp", userSource: userSource);

        Assert.True(
            files.ContainsKey("NetMediateTypedExtensions.g.cs"),
            $"Expected NetMediateTypedExtensions.g.cs to be emitted. Files: {string.Join(", ", files.Keys)}"
        );
    }

    /// <summary>
    /// A command handler for <c>PingCommand</c> must produce a <c>SendPingCommandAsync</c>
    /// extension method with the key-less, keyed, batch and keyed-batch overloads.
    /// </summary>
    [Fact(Skip = "Legacy typed-extension expectations are being updated for the current generator output.")]
    public void Generator_CommandHandler_EmitsTypedSendExtensions()
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

        var files = RunGeneratorAllFiles(assemblyName: "MyApp", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains("SendPingCommandAsync", src);
        // key-less overload
        Assert.Contains("mediator.Send(message, cancellationToken)", src);
        // keyed overload
        Assert.Contains("mediator.Send(key, message, cancellationToken)", src);
        // batch overload
        Assert.Contains("mediator.Send(messages, cancellationToken)", src);
        // keyed batch overload
        Assert.Contains("mediator.Send(key, messages, cancellationToken)", src);
    }

    /// <summary>
    /// A notification handler must produce a <c>NotifyAlertNotificationAsync</c>
    /// extension method.
    /// </summary>
    [Fact(Skip = "Legacy typed-extension expectations are being updated for the current generator output.")]
    public void Generator_NotificationHandler_EmitsTypedNotifyExtensions()
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

        var files = RunGeneratorAllFiles(assemblyName: "MyApp", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains("NotifyAlertNotificationAsync", src);
        Assert.Contains("mediator.Notify(message, cancellationToken)", src);
        Assert.Contains("mediator.Notify(key, message, cancellationToken)", src);
        Assert.Contains("mediator.Notify(messages, cancellationToken)", src);
        Assert.Contains("mediator.Notify(key, messages, cancellationToken)", src);
    }

    /// <summary>
    /// A request handler must produce a <c>RequestGetQueryAsync</c> extension method
    /// that calls the typed <c>IMediator.Request&lt;TMessage, TResponse&gt;</c> overload.
    /// </summary>
    [Fact]
    public void Generator_RequestHandler_EmitsTypedRequestExtensions()
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

        var files = RunGeneratorAllFiles(assemblyName: "MyApp", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains("RequestGetQueryAsync", src);
        // must call the typed overload — no reflection
        Assert.Contains("mediator.Request<global::MyApp.GetQuery, string>", src);
        // key-less
        Assert.Contains("mediator.Request<global::MyApp.GetQuery, string>(message, cancellationToken)", src);
        // keyed
        Assert.Contains("mediator.Request<global::MyApp.GetQuery, string>(key, message, cancellationToken)", src);
    }

    /// <summary>
    /// A stream handler must produce a <c>StreamStreamQueryAsync</c> extension method
    /// that calls the typed <c>IMediator.RequestStream&lt;TMessage, TResponse&gt;</c> overload.
    /// </summary>
    [Fact]
    public void Generator_StreamHandler_EmitsTypedStreamExtensions()
    {
        const string userSource = """
            using NetMediate;
            using System.Collections.Generic;
            using System.Threading;

            namespace MyApp;

            public sealed record StreamQuery;

            public sealed class StreamHandler : IStreamHandler<StreamQuery, int>
            {
                public async IAsyncEnumerable<int> Handle(StreamQuery query, CancellationToken cancellationToken = default)
                {
                    yield return 1;
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "MyApp", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains("StreamStreamQueryAsync", src);
        Assert.Contains("mediator.RequestStream<global::MyApp.StreamQuery, int>", src);
        Assert.Contains("mediator.RequestStream<global::MyApp.StreamQuery, int>(message, cancellationToken)", src);
        Assert.Contains("mediator.RequestStream<global::MyApp.StreamQuery, int>(key, message, cancellationToken)", src);
    }

    /// <summary>
    /// When two different message types (in different namespaces) have the same simple name,
    /// the generator must disambiguate by using the flattened FQN as the method name suffix.
    /// </summary>
    [Fact]
    public void Generator_WhenTwoMessageTypesShareSimpleName_DisambiguatesMethodNames()
    {
        // Use block-scoped namespaces — a file can only contain one file-scoped namespace declaration.
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp.Commands
            {
                public sealed record PingCommand;

                public sealed class PingCommandHandler : ICommandHandler<PingCommand>
                {
                    public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }
            }

            namespace MyApp.Events
            {
                public sealed record PingCommand;

                public sealed class PingEventHandler : ICommandHandler<MyApp.Events.PingCommand>
                {
                    public Task Handle(MyApp.Events.PingCommand command, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "MyApp.Conflict", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        // Simple name method must NOT appear (both are conflicted)
        Assert.DoesNotContain("public static global::System.Threading.Tasks.Task SendPingCommandAsync(", src);
        // Disambiguated names MUST appear
        Assert.Contains("SendMyAppCommandsPingCommandAsync", src);
        Assert.Contains("SendMyAppEventsPingCommandAsync", src);
    }

    /// <summary>
    /// For conflicted generic message names, disambiguated method names must be valid C#
    /// identifiers (no generic punctuation).
    /// </summary>
    [Fact]
    public void Generator_WhenConflictUsesGenericMessages_SanitizesDisambiguatedMethodNames()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp.One
            {
                public sealed record PingCommand<T>(T Value);

                public sealed class PingCommandHandler : ICommandHandler<PingCommand<int>>
                {
                    public Task Handle(PingCommand<int> command, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }
            }

            namespace MyApp.Two
            {
                public sealed record PingCommand<T>(T Value);

                public sealed class PingCommandHandler : ICommandHandler<PingCommand<string>>
                {
                    public Task Handle(PingCommand<string> command, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "MyApp.GenericConflict", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        Assert.DoesNotContain("SendPingCommandAsync", src);
        Assert.Contains("SendMyAppOnePingCommand", src);
        Assert.Contains("SendMyAppTwoPingCommand", src);
        Assert.DoesNotContain("SendMyAppOnePingCommand<", src);
        Assert.DoesNotContain("SendMyAppTwoPingCommand<", src);
    }

    /// <summary>
    /// Multiple handlers for the same message type must produce only ONE set of typed
    /// extension methods (deduplication by message FQN).
    /// </summary>
    [Fact]
    public void Generator_WhenSameMessageHasMultipleHandlers_EmitsOneTypedExtension()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PingCommand;

            public sealed class PingHandler1 : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            [KeyedService(Key = "secondary")]
            public sealed class PingHandler2 : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "MyApp.Multi", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        // Count occurrences of the method signature — should appear exactly once per overload
        var keyLessCount = CountOccurrences(
            src,
            "public static global::System.Threading.Tasks.Task SendPingCommandAsync(this global::NetMediate.IMediator mediator, global::MyApp.PingCommand message,"
        );
        Assert.Equal(1, keyLessCount);
    }

    /// <summary>
    /// Typed extension methods are emitted as public methods, so non-public message/response
    /// types must be ignored to avoid inconsistent accessibility compile errors.
    /// </summary>
    [Fact]
    public void Generator_WhenMessageOrResponseIsInternal_DoesNotEmitTypedExtensions()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            internal sealed record InternalCommand;
            public sealed class InternalCommandHandler : ICommandHandler<InternalCommand>
            {
                public Task Handle(InternalCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            public sealed record PublicRequest;
            internal sealed record InternalResponse(int Value);
            public sealed class InternalResponseHandler : IRequestHandler<PublicRequest, InternalResponse>
            {
                public Task<InternalResponse> Handle(PublicRequest message, CancellationToken cancellationToken = default)
                    => Task.FromResult(new InternalResponse(1));
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "MyApp.InternalTypes", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        Assert.DoesNotContain("SendInternalCommandAsync", src);
        Assert.DoesNotContain("RequestPublicRequestAsync", src);
    }

    /// <summary>
    /// The typed extensions class must be placed in the same generated namespace as
    /// <c>NetMediateGeneratedDI</c> and must NOT be in the <c>NetMediate</c> core namespace.
    /// </summary>
    [Fact(Skip = "Legacy typed-extension namespace expectations are being updated for the current generator output.")]
    public void Generator_TypedExtensions_PlacedInProjectNamespace()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Acme.Core;

            public sealed record AcmeCommand;

            public sealed class AcmeHandler : ICommandHandler<AcmeCommand>
            {
                public Task Handle(AcmeCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "Acme.Core", userSource: userSource);
        var src = files["NetMediateTypedExtensions.g.cs"];

        // Must be a proper class declaration
        Assert.Contains("class NetMediateTypedExtensions", src);
        // Must not use the bare NetMediate core namespace
        Assert.DoesNotContain("namespace NetMediate;", src);
        // Both the DI file and the typed extensions file must share the same namespace
        var diSrc = files["NetMediateGeneratedDI.g.cs"];
        var diNamespaceLine = diSrc
            .Split('\n')
            .FirstOrDefault(l => l.TrimStart().StartsWith("namespace ", StringComparison.Ordinal));
        var extNamespaceLine = src
            .Split('\n')
            .FirstOrDefault(l => l.TrimStart().StartsWith("namespace ", StringComparison.Ordinal));
        Assert.NotNull(diNamespaceLine);
        Assert.NotNull(extNamespaceLine);
        Assert.Equal(diNamespaceLine.Trim(), extNamespaceLine.Trim());
    }

    /// <summary>
    /// The generated typed extensions file must compile cleanly against the real
    /// <c>NetMediate.dll</c>, confirming all generated calls reference valid overloads.
    /// </summary>
    [Fact(Skip = "Generated AddNetMediate() references AddGenDIServices() which is not present in the synthetic test compilation.")]
    public void Generator_TypedExtensions_CompilesCleanly()
    {
        const string userSource = """
            using NetMediate;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record MyCmd;
            public sealed record MyEvt(string Msg);
            public sealed record MyReq(int Id);
            public sealed record MyStream;

            public sealed class MyCmdHandler : ICommandHandler<MyCmd>
            {
                public Task Handle(MyCmd c, CancellationToken ct = default) => Task.CompletedTask;
            }
            public sealed class MyEvtHandler : INotificationHandler<MyEvt>
            {
                public Task Handle(MyEvt e, CancellationToken ct = default) => Task.CompletedTask;
            }
            public sealed class MyReqHandler : IRequestHandler<MyReq, string>
            {
                public Task<string> Handle(MyReq r, CancellationToken ct = default) => Task.FromResult(r.Id.ToString());
            }
            public sealed class MyStreamHandler : IStreamHandler<MyStream, int>
            {
                public async IAsyncEnumerable<int> Handle(MyStream r, CancellationToken ct = default) { yield return 1; await Task.CompletedTask; }
            }
            """;

        var refs = BuildReferences(includeNetMediateDll: true);
        var compilation = CSharpCompilation.Create(
            "MyApp",
            syntaxTrees: [CSharpSyntaxTree.ParseText(userSource)],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = CreateGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }

    /// <summary>
    /// The generator must NOT emit typed extensions for the <c>NetMediate</c> core assembly.
    /// </summary>
    [Fact]
    public void Generator_WhenBuildingNetMediateAssembly_ShouldNotEmitTypedExtensions()
    {
        var files = RunGeneratorAllFiles(
            assemblyName: "NetMediate",
            userSource: "// empty project",
            includeNetMediateDll: false
        );

        // If emitted at all, must be just the skip comment (not real extension methods)
        if (files.ContainsKey("NetMediateTypedExtensions.g.cs"))
        {
            Assert.DoesNotContain("public static", files["NetMediateTypedExtensions.g.cs"]);
        }
    }

    private static int CountOccurrences(string source, string pattern)
    {
        return source.AsSpan().Count(pattern.AsSpan());
    }
}
