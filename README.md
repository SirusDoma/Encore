# Encore

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

Run `dotnet run -c Release --project Source/Encore.Benchmarks -- --filter '*'` to run the BenchmarkDotNet suite.  
Add `--job short` after the filter for a shorter run.

<!-- benchmarks:start -->
<!-- benchmarks:end -->

## License

This project is licensed under the [zlib/libpng license](LICENSE).
