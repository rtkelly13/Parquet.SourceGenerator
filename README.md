![Parquet.SourceGenerator](https://raw.githubusercontent.com/rtkelly13/Parquet.SourceGenerator/main/docs/assets/logo.svg)

# Parquet.SourceGenerator

[![Build & E2E Status](https://github.com/rtkelly13/Parquet.SourceGenerator/actions/workflows/ci.yml/badge.svg)](https://github.com/rtkelly13/Parquet.SourceGenerator/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Parquet.SourceGenerator.svg)](https://www.nuget.org/packages/Parquet.SourceGenerator)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/LICENSE)

A zero-reflection C# Roslyn source generator that emits Parquet serializers and deserializers at
compile time, targeting [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) low-level
primitives.

---

## ⚠️ Status: Early Release

This is early work. Treat the API as evolving.

- **Available on NuGet.** Published as [`Parquet.SourceGenerator`](https://www.nuget.org/packages/Parquet.SourceGenerator).
- **Not benchmarked.** The design avoids reflection and should compare well against
  expression-tree serialization, but no benchmark results have been published. Run the suite
  yourself (see [Contributing](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/CONTRIBUTING.md)) rather than trusting a number here.
- **Native AOT is exercised in CI, with caveats.** Every run publishes
  `Parquet.SourceGenerator.AotTest` with `-r linux-x64`, which puts the ILCompiler through the
  generated code, then executes the resulting native binary — it round-trips a Parquet stream and
  throws on mismatch. Two limits worth knowing:
  - `linux-x64` only. Other runtime identifiers are untested.
  - **Parquet.Net itself is not AOT-clean.** The publish reports `IL2104` (trim warnings) and
    `IL3053` (AOT analysis warnings) against `Parquet.dll` 6.0.3. The generated code is
    reflection-free, but it sits on a library that is not, so what this proves is that *the paths
    the test exercises* work natively — not that any use of this generator will. Exercise your own
    schema under AOT before relying on it.

### Known limitations

| Area | Status |
|:--- |:--- |
| Nanosecond timestamps | **Not offered.** Parquet.Net tops out at microseconds, so the enum member was removed rather than silently writing a coarser column |
| Nested collections (`List<T>`, dictionaries) | **Unsupported**; unsupported property types currently fail at runtime rather than compile time |

<!-- BENCHMARK_TABLE_START -->
## ⚡ Performance & Benchmarks

Zero-reflection C# source generation vs **`ParquetSerializer` v6** reflection baseline:

| Operation | Scale | Reflection Baseline | Source Generator | Speedup | Memory Reduction |
|:--- |:---:|:---:|:---:|:---:|:---:|
| **File Serialization (Write)** | 100,000 items | 7.71 ms (12.63 MB) | **3.65 ms** (**7.02 MB**) | ⚡ **2.1x faster** | 📉 **44% less memory** |
| **Streaming Batched Write** | 100,000 items | 7.71 ms (12.63 MB) | **4.41 ms** (**5.45 MB**) | ⚡ **1.8x faster** | 📉 **57% less memory** |
| **File Deserialization (Read)** | 10,000 items | 2.90 ms (4.96 MB) | **1.44 ms** (**1.60 MB**) | ⚡ **2.0x faster** | 📉 **68% less memory** |
| **Guid Serialization** | 10,000 items | 1.47 ms (2.95 MB) | **910.8 μs** (**1.81 MB**) | ⚡ **1.6x faster** | 📉 **39% less memory** |

> 📌 **Note**: BenchmarkDotNet results captured on GitHub Actions. Detailed multi-scale reports (1K, 10K, 100K, 1M rows) are in [docs/BENCHMARKS.md](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/docs/BENCHMARKS.md).

<!-- BENCHMARK_TABLE_END -->

---

## ✨ Features

- **Zero reflection**: schema, serializers and deserializers are emitted at compile time.
- **Low-level Parquet.Net primitives**: writes via `ParquetWriter`/row-group writers rather than
  the reflection-based `ParquetSerializer`.
- **Row-group streaming**: `WriteParquetBatchedAsync` writes in fixed-size row groups, so large
  sequences never need to be materialized in full.
- **`IAsyncEnumerable<T>` streaming**: stream items straight into chunked row groups.
- **Parallel reader**: `ReadParquetParallelAsync` distributes object construction across row
  groups.
- **Type support**: `Guid`, `DateTime`, `TimeSpan`, `Enum`, `decimal`, `byte[]`, `string`,
  primitives, and `Nullable<T>`.
- **Compile-time diagnostics**: `PARQ001`–`PARQ005` (see below).

---

## 📦 Installation

Install `Parquet.SourceGenerator` and `Parquet.Net` into your project:

```bash
dotnet add package Parquet.SourceGenerator
dotnet add package Parquet.Net
```

`Parquet.SourceGenerator.Attributes` arrives automatically as a dependency of the generator. `Parquet.Net` is required because the generated code targets its low-level primitives directly.

To build from source:

```bash
git clone https://github.com/rtkelly13/Parquet.SourceGenerator.git
cd Parquet.SourceGenerator
dotnet build Parquet.SourceGenertor.sln --configuration Release
```

---

## 📖 Quick Start Example

```csharp
using System;
using Parquet.SourceGenerator;

[ParquetSerializable]
public partial record UserEvent
{
    [ParquetColumn("event_id", Order = 1)]
    public Guid Id { get; init; }

    [ParquetColumn("username", Order = 2)]
    public string Username { get; init; } = string.Empty;

    [ParquetColumn("timestamp", Order = 3)]
    [ParquetTimestamp(ParquetTimestampUnit.Microseconds)]
    public DateTime Timestamp { get; init; }

    [ParquetColumn("duration", Order = 4)]
    public TimeSpan Duration { get; init; }
}
```

### Writing Parquet Files
```csharp
List<UserEvent> events = GetEvents();
using var stream = File.Create("events.parquet");

// Simple write
await events.WriteParquetAsync(stream);

// Streaming write in fixed 10,000 row group chunks
await events.WriteParquetBatchedAsync(stream, rowGroupSize: 10_000);

// Stream directly from IAsyncEnumerable<T>
IAsyncEnumerable<UserEvent> eventStream = GetAsyncEventStream();
await eventStream.WriteParquetAsync(stream, rowGroupSize: 10_000);
```

### Reading Parquet Files (Sequential & Multi-Core Parallel)
```csharp
using var stream = File.OpenRead("events.parquet");

// Sequential read
List<UserEvent> events = await UserEventParquetExtensions.ReadParquetAsync(stream);

// Multi-core parallel read across row groups
List<UserEvent> parallelEvents = await UserEventParquetExtensions.ReadParquetParallelAsync(stream, maxDegreeOfParallelism: 4);

// Read from an in-memory byte buffer
ReadOnlyMemory<byte> buffer = File.ReadAllBytes("events.parquet");
List<UserEvent> memEvents = await UserEventParquetExtensions.ReadParquetAsync(buffer);
```

### Custom Configuration (`ParquetSerializerOptions`)
```csharp
var options = new ParquetSerializerOptions
{
    RowGroupSize = 25_000,
    MaxDegreeOfParallelism = 8,
    CompressionMethod = ParquetCompressionMethod.Zstd
};

await events.WriteParquetBatchedAsync(stream, options: options);
```

`CompressionMethod` accepts `None`, `Snappy` (default), `Gzip`, `Lz4`, `Brotli` and `Zstd`.

---

## 🛡️ Compiler Diagnostics & Guardrails

| Diagnostic ID | Severity | Description |
|:--- |:---:|:--- |
| **`PARQ001`** | **Error** | Target type decorated with `[ParquetSerializable]` must be declared as `partial`. |
| **`PARQ002`** | **Error** | Duplicate `[ParquetColumn]` column names detected on model. |
| **`PARQ003`** | **Warning** | Target type has no valid public serializable properties or fields. |
| **`PARQ004`** | **Warning** | Non-public property decorated with `[ParquetColumn]` will be ignored. |
| **`PARQ005`** | **Error** | Invalid `[ParquetDecimal]` precision or scale parameters (precision must be `>= scale` and `<= 38`). |

---

## 🤝 Contributing & Community

Contributions are welcome! Please review our community guidelines:

- 📖 **[Contributing Guide](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/CONTRIBUTING.md)**: Build setup, testing, and pull request guidelines.
- 📜 **[Code of Conduct](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/CODE_OF_CONDUCT.md)**: Community behavior standards.
- 🛡️ **[Security Policy](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/SECURITY.md)**: Security vulnerability disclosure process.
- 📝 **[Changelog](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/CHANGELOG.md)**: Version history and feature release notes.
- 🏗️ **[Design documentation](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/docs/INDEX.md)**: Architecture, attribute API, generator pipeline,
  and testing strategy.

---

## 📄 License

This project is licensed under the [MIT License](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/LICENSE).
