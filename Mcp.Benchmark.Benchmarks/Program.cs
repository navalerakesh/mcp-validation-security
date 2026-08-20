using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Mcp.Benchmark.Benchmarks.ValidatorBenchmarks).Assembly).Run(args);
