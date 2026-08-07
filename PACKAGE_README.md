# Parquet.SourceGenerator

A zero-reflection C# Roslyn source generator that emits Parquet serializers and deserializers at compile time, targeting [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) low-level primitives.

## 📦 Installation

```bash
dotnet add package Parquet.SourceGenerator
dotnet add package Parquet.Net
```

`Parquet.SourceGenerator.Attributes` is automatically included as a dependency. `Parquet.Net` must be referenced directly by your project.

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

// Chunked row-group streaming write
await events.WriteParquetBatchedAsync(stream, rowGroupSize: 10_000);

// Stream directly from IAsyncEnumerable<T>
IAsyncEnumerable<UserEvent> eventStream = GetAsyncEventStream();
await eventStream.WriteParquetAsync(stream, rowGroupSize: 10_000);
```

### Reading Parquet Files (Sequential & Parallel)

```csharp
using var stream = File.OpenRead("events.parquet");

// Sequential read
List<UserEvent> events = await UserEventParquetExtensions.ReadParquetAsync(stream);

// Multi-core parallel read across row groups
List<UserEvent> parallelEvents = await UserEventParquetExtensions.ReadParquetParallelAsync(stream, maxDegreeOfParallelism: 4);

// Read from in-memory byte buffer
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

Compression options: `None`, `Snappy` (default), `Gzip`, `Lz4`, `Brotli`, `Zstd`.

<!-- BENCHMARK_TABLE_START -->
## ⚡ Performance & Benchmarks

Zero-reflection C# source generation vs **`ParquetSerializer` v6** reflection baseline:

| Operation | Scale | Reflection Baseline | Source Generator | Speedup | Memory Reduction |
|:--- |:---:|:---:|:---:|:---:|:---:|
| **File Serialization (Write)** | 100,000 items | 7.71 ms (12.63 MB) | **3.65 ms** (**7.02 MB**) | ⚡ **2.1x faster** | 📉 **44% less memory** |
| **Streaming Batched Write** | 100,000 items | 7.71 ms (12.63 MB) | **4.41 ms** (**5.45 MB**) | ⚡ **1.8x faster** | 📉 **57% less memory** |
| **File Deserialization (Read)** | 100,000 items | 5.05 ms (4.62 MB) | **6.29 ms** (**10.91 MB**) | 1.25x baseline | 2.36x alloc |
| **Guid Serialization** | 10,000 items | 1.47 ms (2.95 MB) | **910.8 μs** (**1.81 MB**) | ⚡ **1.6x faster** | 📉 **39% less memory** |

> 📌 **Note**: BenchmarkDotNet results captured on GitHub Actions. Detailed multi-scale reports (1K, 10K, 100K, 1M rows) are in [docs/BENCHMARKS.md](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/docs/BENCHMARKS.md).

<!-- BENCHMARK_TABLE_END -->

## 🛡️ Compiler Diagnostics

| Diagnostic ID | Severity | Description |
|:--- |:---:|:--- |
| **`PARQ001`** | **Error** | Target type decorated with `[ParquetSerializable]` must be declared as `partial`. |
| **`PARQ002`** | **Error** | Duplicate `[ParquetColumn]` column names detected on model. |
| **`PARQ003`** | **Warning** | Target type has no valid public serializable properties or fields. |
| **`PARQ004`** | **Warning** | Non-public property decorated with `[ParquetColumn]` will be ignored. |
| **`PARQ005`** | **Error** | Invalid `[ParquetDecimal]` precision or scale parameters. |

## 🔗 Links & Resources

* [GitHub Repository](https://github.com/rtkelly13/Parquet.SourceGenerator)
* [Design Documentation](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/docs/INDEX.md)
* [Contributing Guide](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/CONTRIBUTING.md)
* [MIT License](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/LICENSE)
