using System.Runtime.InteropServices;

namespace NetMediate.Tests;

/// <summary>
/// Always-running test that captures the hardware and runtime environment so that
/// benchmark results can be correlated to the machine they were produced on.
/// Prints <c>SYSTEM_INFO key=value</c> lines to standard test output.
/// </summary>
public sealed class BenchmarkSystemInfoTests(ITestOutputHelper output)
{
    [Fact]
    public void PrintSystemConfiguration()
    {
        var tfm = AppContext.TargetFrameworkName ?? "unknown";
        var runtime = RuntimeInformation.FrameworkDescription;
        var os = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.ProcessArchitecture;
        var cpuCount = Environment.ProcessorCount;
        var gcMemory = GC.GetGCMemoryInfo();
        var totalRamMb = gcMemory.TotalAvailableMemoryBytes / 1024 / 1024;

        output.WriteLine($"SYSTEM_INFO tfm={tfm}");
        output.WriteLine($"SYSTEM_INFO runtime={runtime}");
        output.WriteLine($"SYSTEM_INFO os={os}");
        output.WriteLine($"SYSTEM_INFO arch={arch}");
        output.WriteLine($"SYSTEM_INFO logical_cpus={cpuCount}");
        output.WriteLine($"SYSTEM_INFO total_ram_mb={totalRamMb}");
        output.WriteLine($"SYSTEM_INFO is_64bit_process={Environment.Is64BitProcess}");
        output.WriteLine($"SYSTEM_INFO dotnet_sdk={GetSdkVersion()}");

        // Always pass — this is a diagnostics-only test.
        Assert.True(true);
    }

    private static string GetSdkVersion()
    {
        // The SDK version is not directly available at runtime; use the framework description
        // as the next best signal. The dotnet --version output is captured in CI logs.
        return System.Environment.Version.ToString();
    }
}
