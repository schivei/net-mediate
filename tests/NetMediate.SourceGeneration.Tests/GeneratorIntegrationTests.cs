using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NetMediate.Moq;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

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
        try
        {
            GeneratorAssemblyLoader.GetProjectBuildDllPath();
        }
        catch (Exception ex)
        {
            var packageRoot = GetSourceGenerationPackageRoot();
            var generatorDll = Path.Combine(
                packageRoot,
                "analyzers",
                "dotnet",
                "cs",
                "NetMediate.SourceGeneration.dll"
            );

            if (!File.Exists(generatorDll))
                throw new FileNotFoundException(
                    $"NetMediate.SourceGeneration.dll not found at '{generatorDll}'. "
                        + $"Ensure the referenced NetMediate.SourceGeneration package contains the analyzer.",
                    generatorDll, ex
                );
        }

        var asm = GeneratorAssemblyLoader.Load();
        var type =
            asm.GetType("NetMediate.SourceGeneration.NetMediateRegistrationGenerator")
            ?? throw new InvalidOperationException(
                "NetMediateRegistrationGenerator type not found in the loaded assembly."
            );

        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
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
        bool includeNetMediateDll = true,
        IEnumerable<MetadataReference>? additionalReferences = null
    )
    {
        var references = BuildReferences(includeNetMediateDll, additionalReferences);

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
        LanguageVersion langVersion = LanguageVersion.CSharp13,
        IEnumerable<MetadataReference>? additionalReferences = null
    )
    {
        var references = BuildReferences(includeNetMediateDll, additionalReferences);

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

    private static List<MetadataReference> BuildReferences(
        bool includeNetMediateDll,
        IEnumerable<MetadataReference>? additionalReferences = null
    )
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
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                continue;
            
            try
            {
                refs.Add(MetadataReference.CreateFromFile(asm.Location));
            }
            catch
            {
                // Ignore assemblies that can't be loaded as metadata references (e.g., native or mixed-mode assemblies).
            }
        }

        // GenDI.dll may not be eagerly loaded into the AppDomain (no test code uses it directly),
        // but it exists in the output directory as a transitive dependency of NetMediate.
        // Add it explicitly so that in-memory compilations can resolve [Injectable(Key=...)] attributes.
        var genDiPath = Path.Combine(AppContext.BaseDirectory, "GenDI.dll");
        if (File.Exists(genDiPath))
        {
            try
            {
                refs.Add(MetadataReference.CreateFromFile(genDiPath));
            }
            catch (IOException)
            {
                // Ignore assemblies that can't be loaded as metadata references (e.g. native or mixed-mode assemblies).
            }
            catch (BadImageFormatException)
            {
                // Ignore assemblies that can't be loaded as metadata references (e.g. native or mixed-mode assemblies).
            }
        }

        if (includeNetMediateDll)
        {
            refs.Add(MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location));
            refs.Add(MetadataReference.CreateFromFile(typeof(ICommandHandler<>).Assembly.Location));
            refs.Add(MetadataReference.CreateFromFile(typeof(NotifierMock).Assembly.Location));
        }

        if (additionalReferences is not null)
            refs.AddRange(additionalReferences);

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
    /// package build), it should emit only the fallback stub and no <c>AddNetMediate()</c>
    /// extension method.
    /// </summary>
    [Fact]
    public void Generator_WhenBuildingNetMediateAssembly_ShouldEmitStubWithoutAddNetMediateExtension()
    {
        var (generatedSource, _) = RunGenerator(
            assemblyName: "NetMediate",
            userSource: "// empty project",
            includeNetMediateDll: false
        );

        Assert.Contains("class NetMediateGeneratedDI", generatedSource);
        Assert.DoesNotContain("AddGenDIServices(", generatedSource);
        Assert.Contains("// No handlers found — no registrations to generate.", generatedSource);
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

    /// <summary>Command handlers use the GenDI-first entrypoint and generate typed send helpers.</summary>
    [Fact]
    public void Generator_WhenUserProjectHasCommandHandler_ShouldChainGenDIServicesAndEmitTypedSendHelper()
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

        var files = RunGeneratorAllFiles("MyApp", userSource);
        var diSrc = files["NetMediateGeneratedDI.g.cs"];
        var typedExtensionsSrc = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains(
            "MyApp.DependencyInjection.GenDIServiceCollectionExtensions.AddGenDIServices(services);",
            diSrc
        );
        Assert.Contains(
            "services.RegisterNetMediate();",
            diSrc
        );
        Assert.DoesNotContain("RegisterCommandHandler", diSrc);
        Assert.Contains("SendPingCommandAsync", typedExtensionsSrc);
    }

    /// <summary>Request handlers use the GenDI-first entrypoint and generate typed request helpers.</summary>
    [Fact]
    public void Generator_WhenUserProjectHasRequestHandler_ShouldChainGenDIServicesAndEmitTypedRequestHelper()
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

        var files = RunGeneratorAllFiles("MyApp", userSource);
        var diSrc = files["NetMediateGeneratedDI.g.cs"];
        var typedExtensionsSrc = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains(
            "MyApp.DependencyInjection.GenDIServiceCollectionExtensions.AddGenDIServices(services);",
            diSrc
        );
        Assert.Contains(
            "services.RegisterNetMediate();",
            diSrc
        );
        Assert.DoesNotContain("RegisterRequestHandler", diSrc);
        Assert.Contains("RequestGetQueryAsync", typedExtensionsSrc);
    }

    /// <summary>Notification handlers use the GenDI-first entrypoint and generate typed notify helpers.</summary>
    [Fact]
    public void Generator_WhenUserProjectHasNotificationHandler_ShouldChainGenDIServicesAndEmitTypedNotifyHelper()
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

        var files = RunGeneratorAllFiles("MyApp", userSource);
        var diSrc = files["NetMediateGeneratedDI.g.cs"];
        var typedExtensionsSrc = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains(
            "MyApp.DependencyInjection.GenDIServiceCollectionExtensions.AddGenDIServices(services);",
            diSrc
        );
        Assert.Contains(
            "services.RegisterNetMediate();",
            diSrc
        );
        Assert.DoesNotContain("RegisterNotificationHandler", diSrc);
        Assert.Contains("NotifyAlertNotification", typedExtensionsSrc);
    }

    [Fact]
    public void Generator_WhenCommandHandlerHasInjectableKeyAttribute_DoesNotEmitKeyedHandlerRegistry()
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

        Assert.DoesNotContain("KeyedHandlerRegistry", generatedSource);
    }

    [Fact]
    public void Generator_WhenRequestHandlerHasInjectableKeyAttribute_DoesNotEmitKeyedHandlerRegistry()
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

        Assert.DoesNotContain("KeyedHandlerRegistry", generatedSource);
    }

    [Fact]
    public void Generator_WhenNotificationHandlerHasInjectableKeyAttribute_DoesNotEmitKeyedHandlerRegistry()
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

        Assert.DoesNotContain("KeyedHandlerRegistry", generatedSource);
    }

    [Fact]
    public void Generator_WhenStreamHandlerHasInjectableKeyAttribute_DoesNotEmitKeyedHandlerRegistry()
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

        Assert.DoesNotContain("KeyedHandlerRegistry", generatedSource);
    }

    /// <summary>
    /// Keyed handlers should not produce source-generated keyed registries.
    /// </summary>
    [Fact]
    public void Generator_MultipleKeyedHandlersSameInterface_DoesNotEmitKeyedHandlerRegistry()
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

        Assert.DoesNotContain("KeyedHandlerRegistry", generatedSource);
    }

    /// <summary>
    /// A transient keyed handler should not produce source-generated keyed registries.
    /// </summary>
    [Fact]
    public void Generator_TransientKeyedHandler_DoesNotEmitKeyedHandlerRegistry()
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

        Assert.DoesNotContain("KeyedHandlerRegistry", generatedSource);
    }

    /// <summary>
    /// A scoped keyed handler should not produce source-generated keyed registries.
    /// </summary>
    [Fact]
    public void Generator_ScopedKeyedHandler_DoesNotEmitKeyedHandlerRegistry()
    {
        const string userSource = """
            using GenDI;
            using NetMediate;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record WorkCommand;

            [Injectable(ServiceLifetime.Scoped, Key = "worker")]
            public sealed class WorkHandler : ICommandHandler<WorkCommand>
            {
                public Task Handle(WorkCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (generatedSource, _) = RunGenerator("MyApp", userSource);

        Assert.DoesNotContain("KeyedHandlerRegistry", generatedSource);
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
    /// Record handlers with a base list must be discovered the same way as class handlers.
    /// </summary>
    [Fact]
    public void Generator_WhenHandlerIsRecord_EmitsRegistrationsAndTypedExtensions()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PingCommand;

            public sealed record PingHandler() : ICommandHandler<PingCommand>
            {
                public Task Handle(PingCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "MyApp.RecordHandler", userSource: userSource);
        var diSrc = files["NetMediateGeneratedDI.g.cs"];
        var typedExtensionsSrc = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains("AddNetMediate", diSrc);
        Assert.Contains("SendPingCommandAsync", typedExtensionsSrc);
    }

    /// <summary>
    /// Generic handler types are type definitions and must be ignored by discovery.
    /// </summary>
    [Fact]
    public void Generator_WhenHandlerTypeIsGeneric_DoesNotRegisterGenericTypeDefinition()
    {
        const string userSource = """
            using NetMediate;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp;

            public sealed record PingCommand;
            public sealed record PongCommand;

            public sealed class GenericHandler<T> : ICommandHandler<T>
            {
                public Task Handle(T command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            public sealed class PongHandler : ICommandHandler<PongCommand>
            {
                public Task Handle(PongCommand command, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var files = RunGeneratorAllFiles(assemblyName: "MyApp.GenericHandler", userSource: userSource);
        var diSrc = files["NetMediateGeneratedDI.g.cs"];
        var typedExtensionsSrc = files["NetMediateTypedExtensions.g.cs"];

        Assert.Contains("AddNetMediate", diSrc);
        Assert.Contains("SendPongCommandAsync", typedExtensionsSrc);
        Assert.DoesNotContain("GenericHandler<", diSrc);
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
        if (files.TryGetValue("NetMediateTypedExtensions.g.cs", out var typedExtensionsSource))
        {
            Assert.DoesNotContain("public static", typedExtensionsSource);
        }
    }
}
