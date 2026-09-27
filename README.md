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
<sub>Last run 2026-09-27 19:45 UTC on [`082b94a`](https://github.com/SirusDoma/Encore/commit/082b94a902035d82be867ee947785c72e29cf542) with the `short` job.</sub>

<details><summary>Environment</summary>

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```

</details>

### Dispatch

In-memory `CommandDispatcher.Dispatch` of an encoded request: decode, filters, handler, encode response.

| Method                                        | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------------- |-----------:|------------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Function handler (request → response)       | 1,062.4 ns |    31.43 ns |   1.72 ns |  1.00 |    0.00 | 0.1335 | 0.1316 |    2264 B |        1.00 |
| Function handler (request only)             |   459.8 ns |   645.96 ns |  35.41 ns |  0.43 |    0.03 | 0.0448 | 0.0439 |     760 B |        0.34 |
| Command-only handler                        |   370.4 ns |    51.64 ns |   2.83 ns |  0.35 |    0.00 | 0.0405 | 0.0401 |     680 B |        0.30 |
| Controller (sync)                           | 1,178.4 ns |   288.95 ns |  15.84 ns |  1.11 |    0.01 | 0.1469 | 0.1450 |    2472 B |        1.09 |
| Controller (async + cancellation)           | 1,131.1 ns |   730.11 ns |  40.02 ns |  1.06 |    0.03 | 0.1583 | 0.1564 |    2664 B |        1.18 |
| Controller via DI (scope per request)       | 1,165.7 ns |    84.21 ns |   4.62 ns |  1.10 |    0.00 | 0.1526 | 0.1450 |    2632 B |        1.16 |
| Function handler + 3 global filters         | 1,155.5 ns |   361.91 ns |  19.84 ns |  1.09 |    0.02 | 0.1507 | 0.1488 |    2536 B |        1.12 |
| Controller + global/class/method filters    | 1,134.3 ns |   321.60 ns |  17.63 ns |  1.07 |    0.01 | 0.1678 | 0.1602 |    2824 B |        1.25 |
| Controller + [Authorize]                    | 1,262.5 ns |   594.90 ns |  32.61 ns |  1.19 |    0.03 | 0.1526 | 0.1450 |    2664 B |        1.18 |
| Filter short-circuit with result            | 1,101.2 ns |   553.81 ns |  30.36 ns |  1.04 |    0.02 | 0.1297 | 0.1278 |    2200 B |        0.97 |
| Handler exception → logger + handler result | 7,695.1 ns | 9,270.58 ns | 508.15 ns |  7.24 |    0.41 | 0.3052 | 0.2899 |    5248 B |        2.32 |
| Fan-out to 3 handlers                       | 1,585.9 ns |   461.13 ns |  25.28 ns |  1.49 |    0.02 | 0.3014 | 0.2995 |    5072 B |        2.24 |

### Payload size

Echo request/response dispatch with a UInt16-prefixed string of `Size` characters.

| Method                  | Size  | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------ |------ |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Function handler echo** | **16**    | **1.150 μs** | **0.5437 μs** | **0.0298 μs** |  **1.00** |    **0.03** | **0.1431** | **0.1411** |   **2.35 KB** |        **1.00** |
| Controller echo       | 16    | 1.122 μs | 0.3892 μs | 0.0213 μs |  0.98 |    0.03 | 0.1545 | 0.1526 |   2.55 KB |        1.09 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **1024**  | **1.236 μs** | **0.3344 μs** | **0.0183 μs** |  **1.00** |    **0.02** | **0.5035** | **0.4959** |    **8.3 KB** |        **1.00** |
| Controller echo       | 1024  | 1.453 μs | 0.4991 μs | 0.0274 μs |  1.18 |    0.02 | 0.5188 | 0.5112 |   8.51 KB |        1.02 |
|                         |       |          |           |           |       |         |        |        |           |             |
| **Function handler echo** | **16384** | **3.720 μs** | **2.6200 μs** | **0.1436 μs** |  **1.00** |    **0.05** | **6.0120** | **1.9989** |   **98.3 KB** |        **1.00** |
| Controller echo       | 16384 | 3.663 μs | 2.4477 μs | 0.1342 μs |  0.99 |    0.05 | 6.0120 | 1.9989 |  98.51 KB |        1.00 |

### TCP

End-to-end over loopback: client framer, `TcpServer`, `TcpSession`, dispatcher and back.

| Method                                          | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------------------------ |---------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| Round-trip, function handler                  | 34.76 μs | 58.055 μs | 3.182 μs |  1.01 |    0.12 | 0.1831 | 0.1221 |   3.36 KB |        1.00 |
| Round-trip, controller                        | 34.37 μs | 43.154 μs | 2.365 μs |  0.99 |    0.10 | 0.1831 | 0.1221 |   3.57 KB |        1.06 |
| Pipelined x16, function handler (per request) | 14.03 μs |  3.770 μs | 0.207 μs |  0.41 |    0.03 | 0.1526 | 0.1221 |   2.86 KB |        0.85 |

### Codec

`DefaultMessageCodec` encode/decode without dispatching.

| Method                             | Mean        | Error       | StdDev     | Gen0   | Allocated |
|----------------------------------- |------------:|------------:|-----------:|-------:|----------:|
| Encode int message               |    85.18 ns |    23.10 ns |   1.266 ns | 0.0291 |     488 B |
| Decode int message               |    70.57 ns |    42.77 ns |   2.344 ns | 0.0119 |     200 B |
| Encode 64-char string message    |   103.15 ns |    47.39 ns |   2.597 ns | 0.0396 |     664 B |
| Decode 64-char string message    |    74.87 ns |    22.82 ns |   1.251 ns | 0.0243 |     408 B |
| Encode nested message (16 items) | 4,106.72 ns |   217.03 ns |  11.896 ns | 0.3586 |    6113 B |
| Decode nested message (16 items) | 5,946.99 ns | 5,030.77 ns | 275.754 ns | 0.6027 |   10193 B |
<!-- benchmarks:end -->

## License

This project is licensed under the [zlib/libpng license](LICENSE).
