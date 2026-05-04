using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using NetMediate.Benchmarks;

// Run all benchmarks in this assembly.
// Use `--filter *CoreDispatch*` to run only the core dispatch suite.
//
// JIT (default):
//   dotnet run -c Release --project tests/NetMediate.Benchmarks/
//
// NativeAOT comparison (requires PublishAot=true; set AotBenchmark=true):
//   dotnet publish tests/NetMediate.Benchmarks/ -c Release -p:AotBenchmark=true -o /tmp/bench-aot
//   /tmp/bench-aot/NetMediate.Benchmarks
//
// CI dry-run (fast validation that benchmark classes compile and run):
//   dotnet run -c Release --project tests/NetMediate.Benchmarks/ -- --job Dry

BenchmarkSwitcher
    .FromAssembly(typeof(CoreDispatchBenchmarks).Assembly)
    .Run(args, DefaultConfig.Instance);
