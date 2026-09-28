# Encore

[![Tests](https://github.com/SirusDoma/Encore/actions/workflows/tests.yml/badge.svg)](https://github.com/SirusDoma/Encore/actions/workflows/tests.yml)

Encore is a TCP framework that provides controller-based design and strongly typed network messages.  
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

Run `dotnet run -c Release --project Source/Encore.Benchmarks -- --filter '*'` to run the benchmark suite. 
Add `--job short` after the filter for a shorter run with less accurracy.

<!-- benchmarks:start -->
<sub>Last run 2026-09-28 10:56 UTC on [`4a979a8`](https://github.com/SirusDoma/Encore/commit/4a979a80ab0be7caeb880db1d38229be3ade73ec) with the `short` job.</sub>

<details><summary>Environment</summary>

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```

</details>

### Dispatch

In-memory `CommandDispatcher.Dispatch` of an encoded request: decode, filters, handler, encode response.

| Method                                        | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------------- |------------:|-----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Function handler (request → response)       |  1,949.6 ns |   575.6 ns |  31.55 ns |  1.00 |    0.02 | 0.1297 | 0.1221 |    2264 B |        1.00 |
| Function handler (request only)             |    789.2 ns |   350.3 ns |  19.20 ns |  0.40 |    0.01 | 0.0448 | 0.0439 |     760 B |        0.34 |
| Command-only handler                        |    634.9 ns |   491.2 ns |  26.92 ns |  0.33 |    0.01 | 0.0401 | 0.0391 |     680 B |        0.30 |
| Controller (sync)                           |  2,345.2 ns |   797.7 ns |  43.73 ns |  1.20 |    0.03 | 0.1450 | 0.1411 |    2472 B |        1.09 |
| Controller (async + cancellation)           |  2,279.0 ns | 1,932.9 ns | 105.95 ns |  1.17 |    0.05 | 0.1564 | 0.1526 |    2664 B |        1.18 |
| Controller via DI (scope per request)       |  2,370.8 ns |   349.5 ns |  19.16 ns |  1.22 |    0.02 | 0.1526 | 0.1373 |    2632 B |        1.16 |
| Function handler + 3 global filters         |  2,283.8 ns |   204.4 ns |  11.21 ns |  1.17 |    0.02 | 0.1488 | 0.1450 |    2536 B |        1.12 |
| Controller + global/class/method filters    |  2,151.1 ns |   812.2 ns |  44.52 ns |  1.10 |    0.03 | 0.1678 | 0.1526 |    2824 B |        1.25 |
| Controller + [Authorize]                    |  2,554.1 ns | 1,042.2 ns |  57.13 ns |  1.31 |    0.03 | 0.1526 | 0.1373 |    2664 B |        1.18 |
| Filter short-circuit with result            |  1,964.8 ns |   598.5 ns |  32.80 ns |  1.01 |    0.02 | 0.1297 | 0.1259 |    2200 B |        0.97 |
| Handler exception → logger + handler result | 13,849.3 ns | 1,334.2 ns |  73.13 ns |  7.11 |    0.10 | 0.3052 | 0.2747 |    5248 B |        2.32 |
| Fan-out to 3 handlers                       |  3,153.2 ns |   566.8 ns |  31.07 ns |  1.62 |    0.03 | 0.3014 | 0.2975 |    5072 B |        2.24 |

### Payload size

Echo request/response dispatch with a UInt16-prefixed string of `Size` characters.

| Method                  | Size  | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------ |------ |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Function handler echo** | **16**    |  **2.189 μs** | **0.5546 μs** | **0.0304 μs** |  **1.00** |    **0.02** | **0.1411** | **0.1373** |   **2.35 KB** |        **1.00** |
| Controller echo       | 16    |  2.089 μs | 0.6297 μs | 0.0345 μs |  0.95 |    0.02 | 0.1526 | 0.1373 |   2.55 KB |        1.09 |
|                         |       |           |           |           |       |         |        |        |           |             |
| **Function handler echo** | **1024**  |  **2.470 μs** | **0.5656 μs** | **0.0310 μs** |  **1.00** |    **0.02** | **0.5035** | **0.4883** |    **8.3 KB** |        **1.00** |
| Controller echo       | 1024  |  2.698 μs | 0.5590 μs | 0.0306 μs |  1.09 |    0.02 | 0.5188 | 0.5035 |   8.51 KB |        1.02 |
|                         |       |           |           |           |       |         |        |        |           |             |
| **Function handler echo** | **16384** | **10.004 μs** | **5.4811 μs** | **0.3004 μs** |  **1.00** |    **0.04** | **6.0120** | **1.9836** |   **98.3 KB** |        **1.00** |
| Controller echo       | 16384 |  9.351 μs | 1.9118 μs | 0.1048 μs |  0.94 |    0.03 | 6.0120 | 1.9836 |  98.51 KB |        1.00 |

### TCP

End-to-end over loopback: client framer, `TcpServer`, `TcpSession`, dispatcher and back.

| Method                                          | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------------------ |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Round-trip, function handler                  | 85.43 μs | 58.34 μs | 3.198 μs |  1.00 |    0.05 | 0.1221 |    3.4 KB |        1.00 |
| Round-trip, controller                        | 81.43 μs | 42.01 μs | 2.303 μs |  0.95 |    0.04 |      - |    3.6 KB |        1.06 |
| Pipelined x16, function handler (per request) | 31.75 μs | 55.56 μs | 3.046 μs |  0.37 |    0.03 | 0.1221 |   2.86 KB |        0.84 |

### Codec

`DefaultMessageCodec` encode/decode without dispatching.

| Method                             | Mean        | Error       | StdDev   | Gen0   | Allocated |
|----------------------------------- |------------:|------------:|---------:|-------:|----------:|
| Encode int message               |    175.6 ns |   109.03 ns |  5.98 ns | 0.0291 |     488 B |
| Decode int message               |    148.7 ns |    22.85 ns |  1.25 ns | 0.0119 |     200 B |
| Encode 64-char string message    |    228.8 ns |    68.09 ns |  3.73 ns | 0.0396 |     664 B |
| Decode 64-char string message    |    165.9 ns |    18.54 ns |  1.02 ns | 0.0243 |     408 B |
| Encode nested message (16 items) |  9,158.2 ns |   136.67 ns |  7.49 ns | 0.3510 |    6113 B |
| Decode nested message (16 items) | 14,601.5 ns | 1,656.88 ns | 90.82 ns | 0.5951 |   10193 B |
<!-- benchmarks:end -->

## License

This project is licensed under the [zlib/libpng license](LICENSE).
