using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.SourceGeneration.Tests;

public class SourceGenerationPackageMetadataTests(PackageFixture fixture) : IClassFixture<PackageFixture>
{
    [Fact]
    public void PackageContainsRequiredFiles()
    {
        var packagePath = fixture.PackagePath;

        using var archive = ZipFile.OpenRead(packagePath);

        Assert.Contains(archive.Entries, e => e.FullName == "LICENSE");
        Assert.Contains(archive.Entries, e => e.FullName == "README.md");
        Assert.Contains(archive.Entries, e => e.FullName == "logo.png");
        Assert.Contains(
            archive.Entries,
            e => e.FullName == "buildTransitive/NetMediate.SourceGeneration.props"
        );
        Assert.Contains(
            archive.Entries,
            e => e.FullName == "analyzers/dotnet/cs/NetMediate.SourceGeneration.dll"
        );
    }

    [Fact]
    public void NuspecContainsRequiredMetadata()
    {
        var packagePath = fixture.PackagePath;

        using var archive = ZipFile.OpenRead(packagePath);
        var nuspecEntry = archive.Entries.First(e => e.FullName.EndsWith(".nuspec"));

        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);

        var ns = nuspec.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = nuspec.Root?.Element(ns + "metadata");

        Assert.NotNull(metadata);
        Assert.Equal("NetMediate.SourceGeneration", metadata.Element(ns + "id")?.Value);
        Assert.Equal("NetMediate.SourceGeneration", metadata.Element(ns + "title")?.Value);
        Assert.Equal("Elton Schivei Costa", metadata.Element(ns + "authors")?.Value);
        Assert.Equal("logo.png", metadata.Element(ns + "icon")?.Value);
        Assert.Equal("README.md", metadata.Element(ns + "readme")?.Value);
        Assert.Equal("true", metadata.Element(ns + "developmentDependency")?.Value);
    }
}
