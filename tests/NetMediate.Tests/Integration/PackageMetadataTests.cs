using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace NetMediate.Tests.Integration;

public class PackageMetadataTests
{
    [Fact]
    public void PackageContainsRequiredFiles()
    {
        // This test assumes the package was built in Release configuration
        var packagePath = GetPackagePath();
        
        if (!File.Exists(packagePath))
        {
            // Skip test if package doesn't exist (e.g., in CI without package build)
            return;
        }

        using var archive = ZipFile.OpenRead(packagePath);
        
        // Verify required files are included
        Assert.Contains(archive.Entries, e => e.FullName == "LICENSE");
        Assert.Contains(archive.Entries, e => e.FullName == "README.md");
        Assert.Contains(archive.Entries, e => e.FullName == "icon.png");
        Assert.Contains(archive.Entries, e => e.FullName.EndsWith(".nuspec"));
    }

    [Fact]
    public void NuspecContainsRequiredMetadata()
    {
        var packagePath = GetPackagePath();
        
        if (!File.Exists(packagePath))
        {
            // Skip test if package doesn't exist
            return;
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var nuspecEntry = archive.Entries.First(e => e.FullName.EndsWith(".nuspec"));
        
        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);
        
        var ns = XNamespace.Get("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd");
        var metadata = nuspec.Root?.Element(ns + "metadata");
        
        Assert.NotNull(metadata);
        
        // Check required metadata elements
        Assert.Equal("NetMediate", metadata.Element(ns + "id")?.Value);
        Assert.Equal("NetMediate", metadata.Element(ns + "title")?.Value);
        Assert.Equal("Elton Schivei Costa", metadata.Element(ns + "authors")?.Value);
        Assert.Equal("icon.png", metadata.Element(ns + "icon")?.Value);
        Assert.Equal("README.md", metadata.Element(ns + "readme")?.Value);
        Assert.Equal("LICENSE", metadata.Element(ns + "license")?.Attribute("type")?.Value == "file" ? metadata.Element(ns + "license")?.Value : null);
        Assert.Equal("https://github.com/schivei/net-mediate", metadata.Element(ns + "projectUrl")?.Value);
        
        // Check repository information
        var repository = metadata.Element(ns + "repository");
        Assert.NotNull(repository);
        Assert.Equal("git", repository.Attribute("type")?.Value);
        Assert.Equal("https://github.com/schivei/net-mediate", repository.Attribute("url")?.Value);
    }

    private static string GetPackagePath()
    {
        // Look for the package in the typical build output location
        var projectDir = Path.GetDirectoryName(typeof(PackageMetadataTests).Assembly.Location);
        var solutionDir = Path.GetFullPath(Path.Combine(projectDir!, "..", "..", "..", "..", ".."));
        var packagesDir = Path.Combine(solutionDir, "src", "NetMediate", "bin", "Release");
        
        if (Directory.Exists(packagesDir))
        {
            var packageFiles = Directory.GetFiles(packagesDir, "NetMediate.*.nupkg");
            if (packageFiles.Length > 0)
            {
                return packageFiles[0];
            }
        }
        
        return string.Empty;
    }
}