using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using System.Diagnostics.CodeAnalysis;

[assembly: ExcludeFromCodeCoverage]

BenchmarkSwitcher
    .FromAssembly(typeof(CoreDispatchBenchmarks).Assembly)
    .Run(args, DefaultConfig.Instance);
