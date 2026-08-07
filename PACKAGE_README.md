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

Automated BenchmarkDotNet performance baseline comparing **`Parquet.SourceGenerator`** against **`ParquetSerializer` v6** reflection baseline:

| Benchmark / Method | Row Count | Execution Time | Speed Ratio | Allocated Memory | Memory Ratio |
|:--- |:---:|:---:|:---:|:---:|:---:|
| `SourceGeneratorRowGroupStreaming` | 1000 | `178.0 μs` | **0.81x** | `484.89 KB` | **0.84x** |
| `ReflectionParquetConvertBatch` | 1000 | `219.4 μs` | 1.00x (Baseline) | `576.38 KB` | 1.00x (Baseline) |
| `SourceGeneratorRowGroupStreaming` | 10000 | `2,033.3 μs` | **0.94x** | `4103.17 KB` | **0.84x** |
| `ReflectionParquetConvertBatch` | 10000 | `2,162.8 μs` | 1.00x (Baseline) | `4896.03 KB` | 1.00x (Baseline) |
| `SourceGeneratorRowGroupStreaming` | 100000 | `19,488.6 μs` | **0.87x** | `38620.92 KB` | **0.81x** |
| `ReflectionParquetConvertBatch` | 100000 | `22,356.6 μs` | 1.00x (Baseline) | `47892.67 KB` | 1.00x (Baseline) |
| `ReflectionParquetConvertBatch` | 1000000 | `22,452.8 μs` | 1.00x (Baseline) | `47893.25 KB` | 1.00x (Baseline) |
| `SourceGeneratorRowGroupStreaming` | 1000000 | `173,629.4 μs` | **7.76x** | `367908.11 KB` | **7.68x** |
| `SourceGeneratorWriteAsync` | 1000 | `38.32 μs` | **0.46x** | `43.56 KB` | **0.49x** |
| `ReflectionParquetSerializerV6Write` | 1000 | `82.99 μs` | 1.00x (Baseline) | `89.72 KB` | 1.00x (Baseline) |
| `SourceGeneratorWriteBatchedAsync` | 1000 | `91.97 μs` | **1.11x** | `202.83 KB` | **2.26x** |
| `SourceGeneratorWriteAsync` | 10000 | `333.65 μs` | **0.38x** | `621.23 KB` | **0.47x** |
| `SourceGeneratorWriteBatchedAsync` | 10000 | `540.83 μs` | **0.62x** | `866.16 KB` | **0.66x** |
| `ReflectionParquetSerializerV6Write` | 10000 | `879.71 μs` | 1.00x (Baseline) | `1319.68 KB` | 1.00x (Baseline) |
| `SourceGeneratorWriteAsync` | 100000 | `3,646.59 μs` | **0.47x** | `7183.96 KB` | **0.56x** |
| `SourceGeneratorWriteBatchedAsync` | 100000 | `4,411.60 μs` | **0.57x** | `5582.38 KB` | **0.43x** |
| `ReflectionParquetSerializerV6Write` | 100000 | `7,712.64 μs` | 1.00x (Baseline) | `12932.61 KB` | 1.00x (Baseline) |
| `ReadSourceGenerator` | 1000 | `81.49 μs` | **0.28x** | `166.37 KB` | **0.27x** |
| `WriteSourceGenerator` | 1000 | `242.05 μs` | **0.84x** | `534.25 KB` | **0.85x** |
| `WriteReflectionParquetConvert` | 1000 | `287.22 μs` | 1.00x (Baseline) | `625.71 KB` | 1.00x (Baseline) |
| `ReadSourceGenerator` | 10000 | `1,438.87 μs` | **0.50x** | `1642.05 KB` | **0.32x** |
| `WriteSourceGenerator` | 10000 | `2,746.88 μs` | **0.95x** | `4452.77 KB` | **0.88x** |
| `WriteReflectionParquetConvert` | 10000 | `2,900.20 μs` | 1.00x (Baseline) | `5074.49 KB` | 1.00x (Baseline) |

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
