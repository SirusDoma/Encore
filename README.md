# Encore

[![Tests](https://github.com/SirusDoma/Encore/actions/workflows/tests.yml/badge.svg)](https://github.com/SirusDoma/Encore/actions/workflows/tests.yml)

Encore is a TCP framework that provides controlled-based design and strongly typed network messages.  
Network messages are encoded and decoded using customizable codec and framed using customizable [message framing](https://blog.stephencleary.com/2009/04/message-framing.html).

This framework was originally built specifically for [Mozart.Encore](https://github.com/SirusDoma/Mozart.Encore).

## Features

- Controller-based routing, and lambda routes for simple handlers.
- Strongly typed, self-documenting network messages and protocols.
- Customizable codec, message framing and session types.
- Include session, authorization, filters and exception handling.
- Runs on Generic Host with dependency injection and proper scoping.

## Build

Install the .NET 10 SDK, then run these commands from the repository root:

1. Restore dependencies: `dotnet restore Encore.sln`
2. Build: `dotnet build Encore.sln -c Release --no-restore -m:2`

## Tests

Run `dotnet test Source/Encore.Tests/Encore.Tests.csproj -c Release` to build and run the test suite.

## Benchmarks

Run `dotnet run -c Release --project Source/Encore.Benchmarks -- --filter '*'` to run the `BenchmarkDotNet` suite. 
Add `--job short` after the filter for a shorter run with less accurracy.

<!-- benchmarks:start -->
<sub>Last run 2026-09-27 19:35 UTC on [`957a4c3`](https://github.com/SirusDoma/Encore/commit/957a4c3ba7048229a7d59795ba0d2ff5de3749ff) with the `short` job.</sub>

<details><summary>Environment</summary>

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 3.61GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```

</details>

### Dispatch

In-memory `CommandDispatcher.Dispatch` of an encoded request: decode, filters, handler, encode response.

| Method                                        | Mean       | Error       | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------------- |-----------:|------------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| Function handler (request → response)       | 1,459.4 ns |   113.47 ns |  6.22 ns |  1.00 |    0.01 | 0.1297 | 0.1221 |    2264 B |        1.00 |
| Function handler (request only)             |   574.5 ns |    36.33 ns |  1.99 ns |  0.39 |    0.00 | 0.0448 | 0.0439 |     760 B |        0.34 |
| Command-only handler                        |   479.2 ns |   128.05 ns |  7.02 ns |  0.33 |    0.00 | 0.0401 | 0.0391 |     680 B |        0.30 |
| Controller (sync)                           | 1,578.8 ns |   122.47 ns |  6.71 ns |  1.08 |    0.01 | 0.1469 | 0.1450 |    2472 B |        1.09 |
| Controller (async + cancellation)           | 1,551.8 ns |   153.23 ns |  8.40 ns |  1.06 |    0.01 | 0.1583 | 0.1564 |    2664 B |        1.18 |
| Controller via DI (scope per request)       | 1,645.1 ns |   114.64 ns |  6.28 ns |  1.13 |    0.01 | 0.1564 | 0.1545 |    2632 B |        1.16 |
| Function handler + 3 global filters         | 1,563.7 ns |   113.78 ns |  6.24 ns |  1.07 |    0.01 | 0.1507 | 0.1488 |    2536 B |        1.12 |
| Controller + global/class/method filters    | 1,612.2 ns |   252.26 ns | 13.83 ns |  1.10 |    0.01 | 0.1678 | 0.1659 |    2824 B |        1.25 |
| Controller + [Authorize]                    | 1,821.6 ns |   776.33 ns | 42.55 ns |  1.25 |    0.03 | 0.1526 | 0.1450 |    2664 B |        1.18 |
| Filter short-circuit with result            | 1,477.0 ns |    70.21 ns |  3.85 ns |  1.01 |    0.00 | 0.1297 | 0.1278 |    2200 B |        0.97 |
| Handler exception → logger + handler result | 9,896.4 ns | 1,425.65 ns | 78.14 ns |  6.78 |    0.05 | 0.3052 | 0.2747 |    5248 B |        2.32 |
| Fan-out to 3 handlers                       | 2,183.0 ns |    39.69 ns |  2.18 ns |  1.50 |    0.01 | 0.3014 | 0.2975 |    5072 B |        2.24 |

### Payload size

Echo request/response dispatch with a UInt16-prefixed string of `Size` characters.

| Method                  | Size  | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------ |------ |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Function handler echo** | **16**    | **1.556 μs** | **0.2320 μs** | **0.0127 μs** |  **1.00** |    **0.01** | **0.1431** | **0.1411** |   **2.35 KB** |        **1.00** |
| Controller echo       | 16    | 1.549 μs | 0.0799 μs | 0.0044 μs |  1.00 |    0.01 | 0.1545 | 0.1526 |   2.55 KB |        1.09 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **1024**  | **1.809 μs** | **0.1654 μs** | **0.0091 μs** |  **1.00** |    **0.01** | **0.5074** | **0.5054** |    **8.3 KB** |        **1.00** |
| Controller echo       | 1024  | 2.311 μs | 5.2192 μs | 0.2861 μs |  1.28 |    0.14 | 0.5188 | 0.5035 |   8.51 KB |        1.02 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **16384** | **6.438 μs** | **1.1906 μs** | **0.0653 μs** |  **1.00** |    **0.01** | **6.0120** | **1.9989** |   **98.3 KB** |        **1.00** |
| Controller echo       | 16384 | 6.623 μs | 0.9538 μs | 0.0523 μs |  1.03 |    0.01 | 6.0120 | 1.9989 |  98.51 KB |        1.00 |

### TCP

End-to-end over loopback: client framer, `TcpServer`, `TcpSession`, dispatcher and back.

| Method                                          | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------------------------ |---------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| Round-trip, function handler                  | 47.18 μs | 50.706 μs | 2.779 μs |  1.00 |    0.07 | 0.1221 |      - |    3.4 KB |        1.00 |
| Round-trip, controller                        | 47.46 μs | 70.516 μs | 3.865 μs |  1.01 |    0.09 | 0.1221 |      - |    3.6 KB |        1.06 |
| Pipelined x16, function handler (per request) | 17.99 μs |  2.068 μs | 0.113 μs |  0.38 |    0.02 | 0.1526 | 0.1221 |   2.86 KB |        0.84 |

### Codec

`DefaultMessageCodec` encode/decode without dispatching.

| Method                             | Mean       | Error     | StdDev   | Gen0   | Allocated |
|----------------------------------- |-----------:|----------:|---------:|-------:|----------:|
| Encode int message               |   118.4 ns |  38.36 ns |  2.10 ns | 0.0291 |     488 B |
| Decode int message               |   104.2 ns |   6.95 ns |  0.38 ns | 0.0119 |     200 B |
| Encode 64-char string message    |   145.5 ns |  25.24 ns |  1.38 ns | 0.0396 |     664 B |
| Decode 64-char string message    |   117.9 ns |   7.45 ns |  0.41 ns | 0.0243 |     408 B |
| Encode nested message (16 items) | 5,993.9 ns | 106.45 ns |  5.83 ns | 0.3586 |    6113 B |
| Decode nested message (16 items) | 8,783.3 ns | 940.57 ns | 51.56 ns | 0.5951 |   10193 B |
<!-- benchmarks:end -->

## License

This project is licensed under the [zlib/libpng license](LICENSE).
