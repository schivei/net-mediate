using System.Collections;
using System.Diagnostics;
// ReSharper disable ClassNeverInstantiated.Global

namespace NetMediate.SourceGeneration.Tests;

[Injectable]
public class PackageFixture : IDisposable
{
    internal string PackagePath { get; } = GetPackagePath();

    public void Dispose()
    {
        if (File.Exists(PackagePath))
        {
            File.Delete(PackagePath);
        }
        
        GC.SuppressFinalize(this);
    }

    private static string GetPackagePath()
    {
        var solutionDir = FindSolutionRoot();

        Configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        
        var packagesDir = Path.Combine(solutionDir, "src", "NetMediate.SourceGeneration", "bin", Configuration);
        ProjectFile = Path.Combine(solutionDir, "src", "NetMediate.SourceGeneration", "NetMediate.SourceGeneration.csproj");
        var packageFile = Path.Combine(packagesDir, "NetMediate.SourceGeneration.1.0.0-testing.nupkg");

        if (!File.Exists(packageFile))
            Pack(solutionDir);
        
        return !File.Exists(packageFile) ?
            throw new Exception("Failed to find NetMediate.SourceGeneration package after packing") :
            packageFile;
    }
    
    private static void Pack(string solutionDir)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"pack {ProjectFile} --configuration {Configuration} /p:Version=1.0-testing",
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = solutionDir,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var key in Environment.GetEnvironmentVariables().Keys)
        {
            var value = Environment.GetEnvironmentVariable(key.ToString());
            processInfo.EnvironmentVariables[key.ToString()] = value;
        }
        
        using var process = Process.Start(processInfo);
        
        process.WaitForExit();
    }

    private static string ProjectFile { get; set; }
    private static string Configuration { get; set; }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "net-mediate.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
