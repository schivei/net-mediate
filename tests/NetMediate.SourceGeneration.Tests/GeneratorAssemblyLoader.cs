using System.Diagnostics.CodeAnalysis;
using System.Reflection;

[assembly: ExcludeFromCodeCoverage]
[assembly: GenDICoveration(false)]

namespace NetMediate.SourceGeneration.Tests;

internal static class GeneratorAssemblyLoader
{
    internal static Assembly Load()
    {
        var copiedGeneratorDll = Path.Combine(AppContext.BaseDirectory, "NetMediate.SourceGeneration.dll");
        if (File.Exists(copiedGeneratorDll))
        {
            var binaryContent = File.ReadAllBytes(copiedGeneratorDll);
            return Assembly.Load(binaryContent);
        }

        return Assembly.Load(GetProjectBuildDllPath());
    }

    internal static byte[] GetProjectBuildDllPath()
    {
        var configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif

        var path = Path.GetFullPath(
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

        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find the generator assembly at path: {path}");

        return File.ReadAllBytes(path);
    }
}
