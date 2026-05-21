using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using System.Diagnostics.CodeAnalysis;

[assembly: ExcludeFromCodeCoverage]
[assembly: GenDICoveration(false)]

BenchmarkSwitcher
    .FromAssembly(typeof(CoreDispatchBenchmarks).Assembly)
    .Run(args, DefaultConfig.Instance);

[ExcludeFromCodeCoverage]
public static partial class Program;
