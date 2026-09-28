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
<sub>Last run 2026-09-28 11:40 UTC on [`a8630b7`](https://github.com/SirusDoma/Encore/commit/a8630b7a4588911ea1a07f8c6223838326b356b4) with the `short` job.</sub>

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

| Method                                        | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------------- |------------:|------------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Function handler (request → response)       |  2,116.5 ns | 6,542.13 ns | 358.60 ns |  1.02 |    0.20 | 0.1221 | 0.1068 |    2264 B |        1.00 |
| Function handler (request only)             |    843.4 ns |    75.04 ns |   4.11 ns |  0.41 |    0.05 | 0.0448 | 0.0439 |     760 B |        0.34 |
| Command-only handler                        |    622.1 ns |   201.47 ns |  11.04 ns |  0.30 |    0.04 | 0.0401 | 0.0391 |     680 B |        0.30 |
| Controller (sync)                           |  2,107.1 ns |   468.40 ns |  25.67 ns |  1.01 |    0.14 | 0.1450 | 0.1411 |    2472 B |        1.09 |
| Controller (async + cancellation)           |  2,247.0 ns |   561.99 ns |  30.80 ns |  1.08 |    0.15 | 0.1564 | 0.1526 |    2664 B |        1.18 |
| Controller via DI (scope per request)       |  2,308.1 ns |   584.78 ns |  32.05 ns |  1.11 |    0.15 | 0.1526 | 0.1373 |    2632 B |        1.16 |
| Function handler + 3 global filters         |  2,117.0 ns |   657.88 ns |  36.06 ns |  1.02 |    0.14 | 0.1488 | 0.1450 |    2536 B |        1.12 |
| Controller + global/class/method filters    |  2,250.0 ns |   580.81 ns |  31.84 ns |  1.08 |    0.15 | 0.1678 | 0.1640 |    2824 B |        1.25 |
| Controller + [Authorize]                    |  2,585.3 ns | 2,007.58 ns | 110.04 ns |  1.24 |    0.17 | 0.1526 | 0.1373 |    2664 B |        1.18 |
| Filter short-circuit with result            |  1,978.7 ns |   287.02 ns |  15.73 ns |  0.95 |    0.13 | 0.1297 | 0.1259 |    2200 B |        0.97 |
| Handler exception → logger + handler result | 13,592.7 ns |   507.84 ns |  27.84 ns |  6.54 |    0.88 | 0.3052 | 0.2899 |    5248 B |        2.32 |
| Fan-out to 3 handlers                       |  3,291.8 ns |   387.89 ns |  21.26 ns |  1.58 |    0.21 | 0.3014 | 0.2975 |    5072 B |        2.24 |

### Payload size

Echo request/response dispatch with a UInt16-prefixed string of `Size` characters.

| Method                  | Size  | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------ |------ |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Function handler echo** | **16**    | **2.052 μs** | **0.8940 μs** | **0.0490 μs** |  **1.00** |    **0.03** | **0.1411** | **0.1373** |   **2.35 KB** |        **1.00** |
| Controller echo       | 16    | 2.082 μs | 0.6661 μs | 0.0365 μs |  1.01 |    0.03 | 0.1526 | 0.1488 |   2.55 KB |        1.09 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **1024**  | **2.462 μs** | **0.4353 μs** | **0.0239 μs** |  **1.00** |    **0.01** | **0.5074** | **0.5035** |    **8.3 KB** |        **1.00** |
| Controller echo       | 1024  | 2.720 μs | 0.4900 μs | 0.0269 μs |  1.10 |    0.01 | 0.5188 | 0.5035 |   8.51 KB |        1.02 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **16384** | **9.715 μs** | **1.8757 μs** | **0.1028 μs** |  **1.00** |    **0.01** | **6.0120** | **1.9836** |   **98.3 KB** |        **1.00** |
| Controller echo       | 16384 | 9.102 μs | 2.4406 μs | 0.1338 μs |  0.94 |    0.01 | 6.0120 | 1.9836 |  98.51 KB |        1.00 |

### TCP

End-to-end over loopback: client framer, `TcpServer`, `TcpSession`, dispatcher and back.

| Method                                          | Mean     | Error      | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------------------------ |---------:|-----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| Round-trip, function handler                  | 73.61 μs |  64.773 μs | 3.550 μs |  1.00 |    0.06 |      - |      - |    3.4 KB |        1.00 |
| Round-trip, controller                        | 80.82 μs | 175.270 μs | 9.607 μs |  1.10 |    0.12 |      - |      - |   3.61 KB |        1.06 |
| Pipelined x16, function handler (per request) | 25.52 μs |   7.677 μs | 0.421 μs |  0.35 |    0.02 | 0.1221 | 0.0610 |   2.86 KB |        0.84 |

### Codec

`DefaultMessageCodec` encode/decode without dispatching.

| Method                             | Mean        | Error       | StdDev   | Gen0   | Allocated |
|----------------------------------- |------------:|------------:|---------:|-------:|----------:|
| Encode int message               |    163.9 ns |     6.78 ns |  0.37 ns | 0.0291 |     488 B |
| Decode int message               |    146.0 ns |    25.34 ns |  1.39 ns | 0.0119 |     200 B |
| Encode 64-char string message    |    214.3 ns |    11.91 ns |  0.65 ns | 0.0396 |     664 B |
| Decode 64-char string message    |    173.5 ns |    19.91 ns |  1.09 ns | 0.0243 |     408 B |
| Encode nested message (16 items) |  9,133.0 ns | 1,297.67 ns | 71.13 ns | 0.3510 |    6113 B |
| Decode nested message (16 items) | 14,507.3 ns | 1,218.68 ns | 66.80 ns | 0.5951 |   10193 B |
<!-- benchmarks:end -->

## License

This project is licensed under the [zlib/libpng license](LICENSE).
