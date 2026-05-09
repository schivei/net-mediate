using System.IO.Compression;
using System.Xml.Linq;

namespace NetMediate.SourceGeneration.Tests;

public class SourceGenerationPackageMetadataTests
{
    [Fact]
    public void PackageContainsRequiredFiles()
    {
        var packagePath = GetPackagePath();

        if (!File.Exists(packagePath))
        {
            Assert.Fail(
                "Package not found in src/NetMediate.SourceGeneration/bin/Release/; run `dotnet build src/NetMediate.SourceGeneration/NetMediate.SourceGeneration.csproj --configuration Release` first."
            );
        }

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
        var packagePath = GetPackagePath();

        if (!File.Exists(packagePath))
        {
            Assert.Fail(
                "Package not found in src/NetMediate.SourceGeneration/bin/Release/; run `dotnet build src/NetMediate.SourceGeneration/NetMediate.SourceGeneration.csproj --configuration Release` first."
            );
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var nuspecEntry = archive.Entries.First(e => e.FullName.EndsWith(".nuspec"));

        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);

        var ns = XNamespace.Get("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd");
        var metadata = nuspec.Root?.Element(ns + "metadata");

        Assert.NotNull(metadata);
        Assert.Equal("NetMediate.SourceGeneration", metadata.Element(ns + "id")?.Value);
        Assert.Equal("NetMediate.SourceGeneration", metadata.Element(ns + "title")?.Value);
        Assert.Equal("Elton Schivei Costa", metadata.Element(ns + "authors")?.Value);
        Assert.Equal("logo.png", metadata.Element(ns + "icon")?.Value);
        Assert.Equal("README.md", metadata.Element(ns + "readme")?.Value);
    }

    [Fact]
    public void BuildTransitivePropsDeclareRequiredIndirectDependencies()
    {
        var packagePath = GetPackagePath();

        if (!File.Exists(packagePath))
        {
            Assert.Fail(
                "Package not found in src/NetMediate.SourceGeneration/bin/Release/; run `dotnet build src/NetMediate.SourceGeneration/NetMediate.SourceGeneration.csproj --configuration Release` first."
            );
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var propsEntry = archive.Entries.First(
            e => e.FullName == "buildTransitive/NetMediate.SourceGeneration.props"
        );

        using var reader = new StreamReader(propsEntry.Open());
        var props = reader.ReadToEnd();

        Assert.Contains("PackageReference", props);
        Assert.Contains("Include=\"NetMediate\"", props);
        Assert.Contains("Include=\"GenDI.SourceGenerator\"", props);
        Assert.Contains("PrivateAssets=\"all\"", props);
    }

    private static string GetPackagePath()
    {
        var projectDir = Path.GetDirectoryName(
            typeof(SourceGenerationPackageMetadataTests).Assembly.Location
        );
        var solutionDir = Path.GetFullPath(Path.Combine(projectDir!, "..", "..", "..", "..", ".."));
        var packagesDir = Path.Combine(solutionDir, "src", "NetMediate.SourceGeneration", "bin", "Release");

        if (Directory.Exists(packagesDir))
        {
            var packageFiles = Directory.GetFiles(
                packagesDir,
                "NetMediate.SourceGeneration.*.nupkg"
            );
            if (packageFiles.Length > 0)
            {
                return packageFiles[0];
            }
        }

        return string.Empty;
    }
}
