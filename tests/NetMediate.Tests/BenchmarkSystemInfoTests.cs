using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetMediate.Tests;

/// <summary>
/// Always-running test that captures the hardware and runtime environment so that
/// benchmark results can be correlated to the machine they were produced on.
/// Prints <c>SYSTEM_INFO key=value</c> lines to standard test output.
///
/// <para>
/// <b>execution_mode</b> is set to <c>jit</c> for standard CoreCLR / Mono runs and
/// <c>nativeaot</c> when the process was published with <c>-p:PublishAot=true</c>.
/// This key can be used to compare JIT vs NativeAOT throughput numbers from the
/// <c>CoreDispatchThroughputTests</c> and other benchmark test classes.
/// </para>
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
        var executionMode = RuntimeFeature.IsDynamicCodeSupported ? "jit" : "nativeaot";

        output.WriteLine($"SYSTEM_INFO tfm={tfm}");
        output.WriteLine($"SYSTEM_INFO runtime={runtime}");
        output.WriteLine($"SYSTEM_INFO execution_mode={executionMode}");
        output.WriteLine($"SYSTEM_INFO os={os}");
        output.WriteLine($"SYSTEM_INFO arch={arch}");
        output.WriteLine($"SYSTEM_INFO logical_cpus={cpuCount}");
        output.WriteLine($"SYSTEM_INFO total_ram_mb={totalRamMb}");
        output.WriteLine($"SYSTEM_INFO is_64bit_process={Environment.Is64BitProcess}");
        output.WriteLine($"SYSTEM_INFO dotnet_version={GetDotNetVersion()}");

        Assert.True(true);
    }

    private static string GetDotNetVersion()
    {
        return Environment.Version.ToString();
    }
}
