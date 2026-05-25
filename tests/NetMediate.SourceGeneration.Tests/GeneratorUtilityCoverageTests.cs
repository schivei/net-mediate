using System.Reflection;

namespace NetMediate.SourceGeneration.Tests;

public sealed partial class GeneratorUtilityCoverageTests
{
    private static readonly Assembly s_generatorAssembly = GeneratorAssemblyLoader.Load();

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
    }
}
