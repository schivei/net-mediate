using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using NetMediate.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(CoreDispatchBenchmarks).Assembly)
    .Run(args, DefaultConfig.Instance);
