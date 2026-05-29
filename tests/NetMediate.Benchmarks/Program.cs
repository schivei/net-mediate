using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using System.Diagnostics.CodeAnalysis;
using NetMediate.Benchmarks;

[assembly: ExcludeFromCodeCoverage]
[assembly: GenDICoveration(false)]

BenchmarkSwitcher
    .FromAssembly(typeof(CoreDispatchBenchmarks).Assembly)
    .Run(args, DefaultConfig.Instance);
