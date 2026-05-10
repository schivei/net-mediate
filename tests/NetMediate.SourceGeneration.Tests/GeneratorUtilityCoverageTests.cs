using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetMediate.SourceGeneration.Tests;

public sealed class GeneratorUtilityCoverageTests
{
    private static readonly System.Reflection.Assembly s_generatorAssembly = GeneratorAssemblyLoader.Load();

    [Fact]
    public void BuildRegistrationArguments_ImplicitOperators_RoundTrip()
    {
        var type = s_generatorAssembly.GetType("NetMediate.SourceGeneration.BuildRegistrationArguments")!;
        var diag = new Dictionary<string, bool> { ["diag"] = true };
        var resilience = new Dictionary<string, bool> { ["retry"] = true };
        var handlerType = CreateCompilation().GetTypeByMetadataName("System.String")!;

        var fromTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == type);
        var tupleArgument = Activator.CreateInstance(
            fromTuple.GetParameters()[0].ParameterType,
            true,
            false,
            diag,
            resilience,
            handlerType
        )!;

        var value = fromTuple.Invoke(null, [tupleArgument])!;

        var toTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType != type);
        var roundTripObject = toTuple.Invoke(null, [value]);
        Assert.NotNull(roundTripObject);
        Assert.True(roundTripObject is System.Runtime.CompilerServices.ITuple);
        var roundTrip = (System.Runtime.CompilerServices.ITuple)roundTripObject;

        Assert.True((bool)roundTrip[0]!);
        Assert.False((bool)roundTrip[1]!);
        Assert.Same(diag, roundTrip[2]);
        Assert.Same(resilience, roundTrip[3]);
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
        var diag = new Dictionary<string, bool> { ["diag"] = true };
        var resilience = new Dictionary<string, bool> { ["retry"] = true };

        var fromTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == type);
        var tupleArgument = Activator.CreateInstance(
            fromTuple.GetParameters()[0].ParameterType,
            "IRequestHandler",
            2,
            args,
            true,
            true,
            diag,
            resilience
        )!;

        var value = fromTuple.Invoke(null, [tupleArgument])!;

        var toTuple = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType != type);
        var roundTripObject = toTuple.Invoke(null, [value]);
        Assert.NotNull(roundTripObject);
        Assert.True(roundTripObject is System.Runtime.CompilerServices.ITuple);
        var roundTrip = (System.Runtime.CompilerServices.ITuple)roundTripObject;

        Assert.Equal("IRequestHandler", roundTrip[0]);
        Assert.Equal(2, roundTrip[1]);
        Assert.Equal(args, roundTrip[2]);
        Assert.True((bool)roundTrip[3]!);
        Assert.True((bool)roundTrip[4]!);
        Assert.Same(diag, roundTrip[5]);
        Assert.Same(resilience, roundTrip[6]);
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
            "NetMediate.SourceGeneration.NetMediatePipelineExecutors.template",
            type.GetField("PipelineExecutorsTemplateResourceName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)
        );
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
}
