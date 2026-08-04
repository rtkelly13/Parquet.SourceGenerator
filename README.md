# Parquet.SourceGenerator

[![Build & E2E Status](https://github.com/rtkelly13/Parquet.SourceGenerator/actions/workflows/ci.yml/badge.svg)](https://github.com/rtkelly13/Parquet.SourceGenerator/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A zero-reflection C# Roslyn source generator that emits Parquet serializers and deserializers at
compile time, targeting [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) low-level
primitives.

---

## ⚠️ Status: pre-release, not yet published

This is early work and has not been released. Treat the API as unstable.

- **Not on NuGet.** There is no published package yet; build from source.
- **Not benchmarked.** The design avoids reflection and should compare well against
  expression-tree serialization, but no benchmark results have been published. Run the suite
  yourself (see [Contributing](CONTRIBUTING.md)) rather than trusting a number here.
- **Native AOT is untested.** The generated code is reflection-free by construction, which is a
  precondition for AOT rather than a verification of it. CI does not yet run `dotnet publish`
  against the AOT toolchain.

### Known limitations

| Area | Status |
|:--- |:--- |
| `ParquetSerializerOptions.CompressionMethod` | Accepted but **not applied** — setting it has no effect |
| `ParquetTimestampUnit.Nanoseconds` | Declared but **not implemented** — falls back to the default |
| Nested collections (`List<T>`, dictionaries) | **Unsupported**; unsupported property types currently fail at runtime rather than compile time |
| Column ordering without explicit `Order` | Alphabetical, **not** declaration order |

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

No published package yet. Build from source:

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
    MaxDegreeOfParallelism = 8
};

await events.WriteParquetBatchedAsync(stream, options: options);
```

> `CompressionMethod` exists on `ParquetSerializerOptions` but is not yet applied — see
> [Known limitations](#known-limitations).

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

- 📖 **[Contributing Guide](CONTRIBUTING.md)**: Build setup, testing, and pull request guidelines.
- 📜 **[Code of Conduct](CODE_OF_CONDUCT.md)**: Community behavior standards.
- 🛡️ **[Security Policy](SECURITY.md)**: Security vulnerability disclosure process.
- 📝 **[Changelog](CHANGELOG.md)**: Version history and feature release notes.
- 🏗️ **[Design documentation](docs/INDEX.md)**: Architecture, attribute API, generator pipeline,
  and testing strategy.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
