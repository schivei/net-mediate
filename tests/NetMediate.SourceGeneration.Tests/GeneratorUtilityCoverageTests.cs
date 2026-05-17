using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetMediate.SourceGeneration.Tests;

public sealed partial class GeneratorUtilityCoverageTests
{
    private static readonly System.Reflection.Assembly s_generatorAssembly = GeneratorAssemblyLoader.Load();

    [Fact]
    public void BuildFrameworkBehaviorArguments_ImplicitOperators_RoundTrip()
    {
        var type = s_generatorAssembly.GetType("NetMediate.SourceGeneration.BuildFrameworkBehaviorArguments")!;
        const string template = "template";
        const string assemblyName = "My.Assembly";
        var handlerType = CreateCompilation().GetTypeByMetadataName("System.String")!;

        var fromTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == type);
        var tupleArgument = Activator.CreateInstance(
            fromTuple.GetParameters()[0].ParameterType,
            template,
            assemblyName,
            true,
            false,
            handlerType
        )!;

        var value = fromTuple.Invoke(null, [tupleArgument])!;

        var toTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType != type);
        var roundTripObject = toTuple.Invoke(null, [value]);
        Assert.NotNull(roundTripObject);
        Assert.True(roundTripObject is System.Runtime.CompilerServices.ITuple);
        var roundTrip = (System.Runtime.CompilerServices.ITuple)roundTripObject;

        Assert.Equal(template, roundTrip[0]);
        Assert.Equal(assemblyName, roundTrip[1]);
        Assert.True((bool)roundTrip[2]!);
        Assert.False((bool)roundTrip[3]!);
        Assert.Same(handlerType, roundTrip[4]);
    }

    [Fact]
    public void ProcessHandlerInterfaceArguments_ImplicitOperators_RoundTrip()
    {
        var type = s_generatorAssembly.GetType("NetMediate.SourceGeneration.ProcessHandlerInterfaceArguments")!;
        var compilation = CreateCompilation();
        var args = ImmutableArray.Create<ITypeSymbol>(
            compilation.GetTypeByMetadataName("System.String")!,
            compilation.GetTypeByMetadataName("System.Int32")!
        );
        const string template = "template";
        const string assemblyName = "My.Assembly";

        var fromTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == type);
        var tupleArgument = Activator.CreateInstance(
            fromTuple.GetParameters()[0].ParameterType,
            template,
            assemblyName,
            "IRequestHandler",
            2,
            args,
            true,
            true
        )!;

        var value = fromTuple.Invoke(null, [tupleArgument])!;

        var toTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType != type);
        var roundTripObject = toTuple.Invoke(null, [value]);
        Assert.NotNull(roundTripObject);
        Assert.True(roundTripObject is System.Runtime.CompilerServices.ITuple);
        var roundTrip = (System.Runtime.CompilerServices.ITuple)roundTripObject;

        Assert.Equal(template, roundTrip[0]);
        Assert.Equal(assemblyName, roundTrip[1]);
        Assert.Equal("IRequestHandler", roundTrip[2]);
        Assert.Equal(2, roundTrip[3]);
        Assert.Equal(args, roundTrip[4]);
        Assert.True((bool)roundTrip[5]!);
        Assert.True((bool)roundTrip[6]!);
    }

    [Fact]
    public void DictionaryExtensions_AddIfNew_DeduplicatesBothDictionaryTypes()
    {
        var type = s_generatorAssembly.GetType("NetMediate.SourceGeneration.DictionaryExtensions")!;
        var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "AddIfNew")
            .ToArray();

        var dictionaryMethod = methods.Single(m => m.GetParameters()[0].ParameterType == typeof(Dictionary<string, bool>));
        var concurrentMethod = methods.Single(m => m.GetParameters()[0].ParameterType == typeof(ConcurrentDictionary<string, bool>));

        var dictionary = new Dictionary<string, bool>();
        dictionaryMethod.Invoke(null, [dictionary, "alpha"]);
        dictionaryMethod.Invoke(null, [dictionary, "alpha"]);

        var concurrent = new ConcurrentDictionary<string, bool>();
        concurrentMethod.Invoke(null, [concurrent, "beta"]);
        concurrentMethod.Invoke(null, [concurrent, "beta"]);

        Assert.Equal(["alpha"], dictionary.Keys);
        Assert.Equal(["beta"], concurrent.Keys);
    }

    [Fact]
    public void Constants_ExposeExpectedTemplateNamesAndTokens()
    {
        var type = s_generatorAssembly.GetType("NetMediate.SourceGeneration.Constants")!;

        Assert.Equal("NetMediate", type.GetField("PackName")!.GetRawConstantValue());
        Assert.Equal("{{Coverage}}", type.GetField("CoverageToken")!.GetRawConstantValue());
        Assert.Equal("{{TypedExtensions}}", type.GetField("TypedExtensionsToken")!.GetRawConstantValue());
        Assert.Equal(
            "NetMediate.SourceGeneration.NetMediateGeneratedDI.template",
            type.GetField("TemplateResourceName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)
        );
        Assert.Equal(
            "NetMediate.SourceGeneration.NetMediateFrameworkBehavior.template",
            type.GetField("TemplateBehaviorResourceName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)
        );
    }

    [Fact]
    public void Constants_RandomNameFrom_GeneratesValidAndStableIdentifier()
    {
        var type = s_generatorAssembly.GetType("NetMediate.SourceGeneration.Constants")!;
        var method = type.GetMethod("RandomNameFrom", BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = new object?[]
        {
            "global::NetMediate.IRequestHandler<global::My.App.SampleMessage, global::My.App.SampleResponse>",
            "TelemetryRequestBehavior",
            null
        };
        var generated = (string)method.Invoke(null, args)!;
        var outName = (string)args[2]!;

        Assert.Equal(generated, outName);
        Assert.Matches(ValidIdentifierRegex(), generated);
        Assert.DoesNotContain(":", generated);
        Assert.DoesNotContain(".", generated);
        Assert.DoesNotContain("<", generated);
        Assert.DoesNotContain(">", generated);

        var args2 = new object?[]
        {
            "global::NetMediate.IRequestHandler<global::My.App.SampleMessage, global::My.App.SampleResponse>",
            "TelemetryRequestBehavior",
            null
        };
        var generated2 = (string)method.Invoke(null, args2)!;
        Assert.Equal(generated, generated2);

        var argsDifferentMessage = new object?[]
        {
            "global::NetMediate.IRequestHandler<global::My.App.OtherMessage, global::My.App.SampleResponse>",
            "TelemetryRequestBehavior",
            null
        };
        var generatedDifferentMessage = (string)method.Invoke(null, argsDifferentMessage)!;
        Assert.NotEqual(generated, generatedDifferentMessage);

        var argsDifferentResponse = new object?[]
        {
            "global::NetMediate.IRequestHandler<global::My.App.SampleMessage, global::My.App.OtherResponse>",
            "TelemetryRequestBehavior",
            null
        };
        var generatedDifferentResponse = (string)method.Invoke(null, argsDifferentResponse)!;
        Assert.NotEqual(generated, generatedDifferentResponse);

        var argsDifferentImplementation = new object?[]
        {
            "global::NetMediate.IStreamHandler<global::My.App.SampleMessage, global::My.App.SampleResponse>",
            "TelemetryStreamBehavior",
            null
        };
        var generatedDifferentImplementation = (string)method.Invoke(null, argsDifferentImplementation)!;
        Assert.NotEqual(generated, generatedDifferentImplementation);
    }

    [Fact]
    public void Constants_GetBehaviorClasses_GeneratesUniqueAndDeterministicNames()
    {
        var constantsType = s_generatorAssembly.GetType("NetMediate.SourceGeneration.Constants")!;
        var registrationType = s_generatorAssembly.GetType("NetMediate.SourceGeneration.BehaviorRegistration")!;
        var template = """
            // <auto-generated />
            namespace {{AssemblyNamespace}};
            [DecoratorFor<{{ImplementationType}}>(Order = {{Order}})]
            public sealed partial class {{RandomName}} : {{BehaviorAbstraction}} { }
            """;

        var registration = Activator.CreateInstance(
            registrationType,
            template,
            "MyApp",
            "IRequestHandler",
            "global::MyApp.SampleMessage",
            "global::MyApp.SampleResponse",
            true,
            true
        )!;

        var method = constantsType.GetMethod(
            "GetBehaviorClasses",
            BindingFlags.Public | BindingFlags.Static
        )!;

        var generated = ((System.Collections.IEnumerable)method.Invoke(null, [registration])!)
            .Cast<object>()
            .Select(entry =>
            {
                var tuple = (System.Runtime.CompilerServices.ITuple)entry;
                return (definition: (string)tuple[0]!, className: (string)tuple[1]!);
            })
            .ToArray();

        Assert.NotEmpty(generated);
        Assert.Equal(generated.Length, generated.Select(x => x.className).Distinct(StringComparer.Ordinal).Count());
        Assert.All(generated, x => Assert.Matches(ValidIdentifierRegex(), x.className));

        var generatedAgain = ((System.Collections.IEnumerable)method.Invoke(null, [registration])!)
            .Cast<object>()
            .Select(entry => (string)((System.Runtime.CompilerServices.ITuple)entry)[1]!)
            .ToArray();
        Assert.Equal(generated.Select(x => x.className), generatedAgain);
    }

    [Fact]
    public void EnumerateReferencedAssemblies_FiltersPackAndCurrentAssemblyNames()
    {
        var compilation = CSharpCompilation.Create(
            "MyApp",
            syntaxTrees: [CSharpSyntaxTree.ParseText("namespace MyApp; public sealed class Marker;")],
            references:
            [
                CreateMetadataReference("NetMediate", "namespace NetMediate; public sealed class RefType;"),
                CreateMetadataReference("MyApp", "namespace MyApp; public sealed class RefType;"),
                CreateMetadataReference("External", "namespace External; public sealed class RefType;"),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generatorType = s_generatorAssembly.GetType("NetMediate.SourceGeneration.NetMediateRegistrationGenerator")!;
        var method = generatorType.GetMethod(
            "EnumerateReferencedAssemblies",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var referencedAssemblies = ((System.Collections.IEnumerable)method.Invoke(null, [compilation])!)
            .Cast<IAssemblySymbol>()
            .Select(symbol => symbol.Name)
            .ToArray();

        Assert.Contains("External", referencedAssemblies);
        Assert.DoesNotContain("MyApp", referencedAssemblies);
        Assert.DoesNotContain("NetMediate", referencedAssemblies);
    }

    [Fact]
    public void EnumerateReferencedAssemblies_WhenCompilationAssemblyNameIsNull_UsesEmptyCurrentAssemblyName()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: null,
            syntaxTrees: [CSharpSyntaxTree.ParseText("namespace Sample; public sealed class Marker;")],
            references:
            [
                CreateMetadataReference("External", "namespace External; public sealed class RefType;"),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generatorType = s_generatorAssembly.GetType("NetMediate.SourceGeneration.NetMediateRegistrationGenerator")!;
        var method = generatorType.GetMethod(
            "EnumerateReferencedAssemblies",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var referencedAssemblies = ((System.Collections.IEnumerable)method.Invoke(null, [compilation])!)
            .Cast<IAssemblySymbol>()
            .Select(symbol => symbol.Name)
            .ToArray();

        Assert.Contains("External", referencedAssemblies);
    }

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            "GeneratorUtilityCoverageTests",
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IIncrementalGenerator).Assembly.Location),
            ]
        );

    private static PortableExecutableReference CreateMetadataReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.Empty(emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        stream.Position = 0;
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    [GeneratedRegex("^[_A-Za-z][_A-Za-z0-9]*$")]
    private static partial Regex ValidIdentifierRegex();
}
