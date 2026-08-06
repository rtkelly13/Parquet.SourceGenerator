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
