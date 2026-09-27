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
<sub>Last run 2026-09-27 19:29 UTC on [`5af0666`](https://github.com/SirusDoma/Encore/commit/5af066631cd0488b1423bb6966190f8c5f498855) with the `short` job.</sub>

<details><summary>Environment</summary>

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 2.30GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```

</details>

### Dispatch

In-memory `CommandDispatcher.Dispatch` of an encoded request: decode, filters, handler, encode response.

| Method                                        | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------------- |------------:|------------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Function handler (request → response)       |  2,057.9 ns |  2,653.8 ns | 145.46 ns |  1.00 |    0.09 | 0.0267 | 0.0229 |    2264 B |        1.00 |
| Function handler (request only)             |    891.9 ns |    394.7 ns |  21.63 ns |  0.43 |    0.03 | 0.0086 | 0.0076 |     760 B |        0.34 |
| Command-only handler                        |    911.4 ns |    204.9 ns |  11.23 ns |  0.44 |    0.03 | 0.0076 | 0.0067 |     680 B |        0.30 |
| Controller (sync)                           |  2,016.0 ns |    368.4 ns |  20.19 ns |  0.98 |    0.06 | 0.0267 | 0.0229 |    2472 B |        1.09 |
| Controller (async + cancellation)           |  2,333.1 ns |  1,756.1 ns |  96.26 ns |  1.14 |    0.08 | 0.0305 | 0.0267 |    2664 B |        1.18 |
| Controller via DI (scope per request)       |  2,261.9 ns |    852.4 ns |  46.72 ns |  1.10 |    0.07 | 0.0305 | 0.0229 |    2632 B |        1.16 |
| Function handler + 3 global filters         |  2,033.7 ns |  1,352.2 ns |  74.12 ns |  0.99 |    0.07 | 0.0267 | 0.0229 |    2536 B |        1.12 |
| Controller + global/class/method filters    |  2,414.9 ns |  1,421.4 ns |  77.91 ns |  1.18 |    0.08 | 0.0305 | 0.0153 |    2824 B |        1.25 |
| Controller + [Authorize]                    |  3,288.8 ns | 12,591.4 ns | 690.18 ns |  1.60 |    0.31 | 0.0305 | 0.0153 |    2664 B |        1.18 |
| Filter short-circuit with result            |  1,997.7 ns |    963.8 ns |  52.83 ns |  0.97 |    0.06 | 0.0248 | 0.0229 |    2200 B |        0.97 |
| Handler exception → logger + handler result | 11,962.0 ns |  1,036.3 ns |  56.81 ns |  5.83 |    0.36 | 0.0610 | 0.0458 |    5248 B |        2.32 |
| Fan-out to 3 handlers                       |  3,602.3 ns |  1,020.2 ns |  55.92 ns |  1.76 |    0.11 | 0.0572 | 0.0534 |    5072 B |        2.24 |

### Payload size

Echo request/response dispatch with a UInt16-prefixed string of `Size` characters.

| Method                  | Size  | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------ |------ |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Function handler echo** | **16**    | **2.138 μs** | **0.6541 μs** | **0.0359 μs** |  **1.00** |    **0.02** | **0.0267** | **0.0229** |   **2.35 KB** |        **1.00** |
| Controller echo       | 16    | 2.255 μs | 0.4440 μs | 0.0243 μs |  1.05 |    0.02 | 0.0305 | 0.0267 |   2.55 KB |        1.09 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **1024**  | **3.040 μs** | **2.1169 μs** | **0.1160 μs** |  **1.00** |    **0.05** | **0.0916** | **0.0763** |    **8.3 KB** |        **1.00** |
| Controller echo       | 1024  | 3.223 μs | 1.5546 μs | 0.0852 μs |  1.06 |    0.04 | 0.0916 | 0.0763 |   8.51 KB |        1.02 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **16384** | **7.605 μs** | **1.7017 μs** | **0.0933 μs** |  **1.00** |    **0.01** | **1.1902** | **1.1597** |   **98.3 KB** |        **1.00** |
| Controller echo       | 16384 | 7.877 μs | 0.3404 μs | 0.0187 μs |  1.04 |    0.01 | 1.1902 | 1.1597 |  98.51 KB |        1.00 |

### TCP

End-to-end over loopback: client framer, `TcpServer`, `TcpSession`, dispatcher and back.

| Method                                          | Mean     | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------------------------ |---------:|----------:|---------:|------:|--------:|----------:|------------:|
| Round-trip, function handler                  | 45.51 μs | 32.605 μs | 1.787 μs |  1.00 |    0.05 |    3.4 KB |        1.00 |
| Round-trip, controller                        | 46.78 μs | 14.834 μs | 0.813 μs |  1.03 |    0.04 |    3.6 KB |        1.06 |
| Pipelined x16, function handler (per request) | 13.07 μs |  6.255 μs | 0.343 μs |  0.29 |    0.01 |   2.86 KB |        0.84 |

### Codec

`DefaultMessageCodec` encode/decode without dispatching.

| Method                             | Mean       | Error       | StdDev    | Gen0   | Allocated |
|----------------------------------- |-----------:|------------:|----------:|-------:|----------:|
| Encode int message               |   126.9 ns |    18.72 ns |   1.03 ns | 0.0057 |     488 B |
| Decode int message               |   101.8 ns |     2.40 ns |   0.13 ns | 0.0024 |     200 B |
| Encode 64-char string message    |   170.0 ns |    19.39 ns |   1.06 ns | 0.0079 |     664 B |
| Decode 64-char string message    |   116.4 ns |    28.34 ns |   1.55 ns | 0.0048 |     408 B |
| Encode nested message (16 items) | 5,732.6 ns |   746.71 ns |  40.93 ns | 0.0687 |    6112 B |
| Decode nested message (16 items) | 8,195.0 ns | 1,847.29 ns | 101.26 ns | 0.1068 |   10192 B |
<!-- benchmarks:end -->

## License

This project is licensed under the [zlib/libpng license](LICENSE).
