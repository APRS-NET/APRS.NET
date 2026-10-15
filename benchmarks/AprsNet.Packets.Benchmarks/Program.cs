using BenchmarkDotNet.Running;

// dotnet run -c Release --project benchmarks/AprsNet.Packets.Benchmarks -- --filter '*'
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
