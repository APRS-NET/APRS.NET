# Benchmarks

`benchmarks/AprsNet.Packets.Benchmarks` measures parse and build speed and allocations with
[BenchmarkDotNet](https://benchmarkdotnet.org). Run them in Release, on a quiet machine:

```sh
dotnet run -c Release --project benchmarks/AprsNet.Packets.Benchmarks -- --filter '*'
dotnet run -c Release --project benchmarks/AprsNet.Packets.Benchmarks -- --filter '*Parse*'   # one class
```

| Class | Measures |
| --- | --- |
| `ParseBenchmarks` | `AprsPacket.Parse` of one line of each common kind, from bytes |
| `CorpusBenchmarks` | Parsing the first 10,000 corpus packets (real traffic's mix of types, lengths and mistakes), per packet |
| `CreateBenchmarks` | `PacketAddress.Create`, which encodes the payload and parses the result back to check it |

CI builds the project but does not run it: shared runners are too noisy for timings to mean anything. Compare a
change against the baseline below on the same machine, and update the baseline when a change moves it on purpose.

## Baseline

15 October 2026, before the 0.1 prerelease. BenchmarkDotNet 0.15.8, .NET 10.0.12, Fedora 44, AMD Ryzen 5 7640U (6
cores, laptop), default job.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| Parse a corpus packet (average of 10,000) | 526 ns | 1.39 KB |
| Parse status | 248 ns | 816 B |
| Parse compressed position | 317 ns | 1,208 B |
| Parse Mic-E | 341 ns | 984 B |
| Parse position (course, speed, altitude) | 357 ns | 1,248 B |
| Parse object | 370 ns | 1,272 B |
| Parse message | 390 ns | 872 B |
| Parse telemetry | 469 ns | 1,128 B |
| Parse complete weather report | 741 ns | 2,608 B |
| Create message | 462 ns | 1.42 KB |
| Create position | 1,094 ns | 2.15 KB |

So one core parses about 1.9 million packets a second, against a full APRS-IS feed of roughly 100 to 200 a
second. `Create` costs about two parses, since it parses its own result to check it.
