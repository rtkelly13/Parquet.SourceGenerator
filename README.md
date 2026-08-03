# Parquet.SourceGenerator

[![Build & E2E Status](https://github.com/ryankelly/Parquet.SourceGenerator/actions/workflows/ci.yml/badge.svg)](https://github.com/ryankelly/Parquet.SourceGenerator/actions/workflows/ci.yml)
[![Benchmark Baseline](https://github.com/ryankelly/Parquet.SourceGenerator/actions/workflows/benchmarks.yml/badge.svg)](https://github.com/ryankelly/Parquet.SourceGenerator/actions/workflows/benchmarks.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Parquet.SourceGenerator.svg)](https://www.nuget.org/packages/Parquet.SourceGenerator)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)]()
[![Native AOT](https://img.shields.io/badge/Native%20AOT-100%25%20Compatible-blue.svg)]()

High-performance, zero-reflection, Native AOT-compatible C# Roslyn Source Generator targeting [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) v6 low-level primitives.

---

## ⚡ Key Features & Performance Gains

- 🚀 **Zero Reflection & Native AOT Ready**: 100% safe for `.NET 8` Native AOT compilation (`PublishAot=true`).
- ⚡ **2.22x Serialization Speedup**: Achieves **2.22x faster** writing times (**3.07 ms** vs **6.97 ms** for 100k rows) compared to compiled expression tree baselines.
- 💾 **-56.7% Memory Reduction**: Streaming row group chunking reduces GC managed memory allocations by **56.7%** (**5.57 MB** vs **12.87 MB**).
- 🔀 **Multi-Core Parallel Reader (`ReadParquetParallelAsync`)**: Parallel object creation across CPU cores (**1.45x faster** on multi-row-group files).
- 🆔 **Native 16-byte `Guid` Binary Encoding**: Direct `ArrayPool<Guid>` struct column buffer transfer with **zero string heap allocations** (**1.58x faster**, **-39.1% memory**).
- ⏱️ **Compact 8-Byte Int64 Timestamps**: Support for `[ParquetTimestamp(ParquetTimestampUnit.Microseconds)]` (**33% smaller column footprint**).
- 🔄 **`IAsyncEnumerable<T>` Streaming**: Asynchronously stream items directly into chunked Parquet row groups without intermediate list allocations.
- ⚙️ **Configurable `ParquetSerializerOptions` API**: Configure `RowGroupSize`, `MaxDegreeOfParallelism`, `CompressionMethod`, and timestamp defaults centrally.
- 📦 **Expanded Data Type Support**: `Guid`, `DateTime`, `TimeSpan`, `Enum`, `Decimal`, `byte[]`, `string`, primitive types, and `Nullable<T>`.

---

## 📦 Installation

Install the generator and attributes packages via NuGet:

```bash
dotnet add package Parquet.SourceGenerator.Attributes
dotnet add package Parquet.SourceGenerator
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

// Zero-copy read from in-memory byte buffer
ReadOnlyMemory<byte> buffer = File.ReadAllBytes("events.parquet");
List<UserEvent> memEvents = await UserEventParquetExtensions.ReadParquetAsync(buffer);
```

### Custom Configuration (`ParquetSerializerOptions`)
```csharp
var options = new ParquetSerializerOptions
{
    RowGroupSize = 25_000,
    MaxDegreeOfParallelism = 8,
    CompressionMethod = ParquetCompressionMethod.Snappy
};

await events.WriteParquetBatchedAsync(stream, options: options);
```

---

## 🛡️ Compiler Diagnostics & Guardrails

| Diagnostic ID | Severity | Description |
|:--- |:---:|:--- |
| **`PARQ001`** | **Error** | Target type decorated with `[ParquetSerializable]` must be declared as `partial`. |
| **`PARQ002`** | **Error** | Duplicate `[ParquetColumn]` column names detected on model. |
| **`PARQ003`** | **Warning** | Target type has no valid public serializable properties or fields. |

---

## 🤝 Contributing & Community

Contributions are welcome! Please review our community guidelines:

- 📖 **[Contributing Guide](CONTRIBUTING.md)**: Build setup, testing, and pull request guidelines.
- 🛡️ **[Security Policy](SECURITY.md)**: Security vulnerability disclosure process.
- 📝 **[Changelog](CHANGELOG.md)**: Version history and feature release notes.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
