using System.Reflection;

namespace NetMediate.SourceGeneration.Tests;

internal static class GeneratorAssemblyLoader
{
    internal static Assembly Load()
    {
        var copiedGeneratorDll = Path.Combine(AppContext.BaseDirectory, "NetMediate.SourceGeneration.dll");
        if (File.Exists(copiedGeneratorDll))
            return Assembly.LoadFrom(copiedGeneratorDll);

        return Assembly.LoadFrom(GetProjectBuildDllPath());
    }

    internal static string GetProjectBuildDllPath()
    {
        var configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif

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
}
