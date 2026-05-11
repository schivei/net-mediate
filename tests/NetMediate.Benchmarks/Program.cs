using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

BenchmarkSwitcher
    .FromAssembly(typeof(CoreDispatchBenchmarks).Assembly)
    .Run(args, DefaultConfig.Instance);
