# Parquet.SourceGenerator

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)]()

High-performance, zero-reflection, Native AOT-compatible C# Roslyn Source Generator for the [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) library.

---

## ⚡ Key Features

- 🚀 **Zero Reflection**: Replaces dynamic reflection-based serialization with statically compiled, strongly-typed column readers and writers.
- ⚡ **Ultra-High Throughput**: Transfers column data directly between primitive arrays and Parquet buffers, dramatically reducing CPU and GC allocation overhead.
- 🛡️ **Native AOT & Trimming Ready**: Fully safe for `.NET 8` and `.NET 9+` Native AOT compilation (`PublishAot=true`).
- 🔍 **Compile-Time Diagnostics**: Catches schema mismatches and unsupported data types at compile time (`PARQ001` - `PARQ099`).
- 💡 **Intuitive API**: Annotate your classes, records, or structs with `[ParquetSerializable]` and `[ParquetColumn]`.

---

## 📖 Quick Example

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
    public DateTime Timestamp { get; init; }
}
```

### Writing Parquet Files
```csharp
List<UserEvent> events = GetEvents();
using var stream = File.Create("events.parquet");

// Zero-reflection compile-time generated stream writer
await events.WriteParquetAsync(stream);
```

### Reading Parquet Files
```csharp
using var stream = File.OpenRead("events.parquet");

// Zero-reflection compile-time generated stream reader
List<UserEvent> events = await UserEventParquetExtensions.ReadParquetAsync(stream);
```

---

## 🚀 Native AOT Sample Application

A fully functional, zero-warning Native AOT sample application is available in [`samples/Parquet.SourceGenerator.SampleAot`](samples/Parquet.SourceGenerator.SampleAot):

```bash
# Run the Native AOT sample project directly
dotnet run --project samples/Parquet.SourceGenerator.SampleAot/Parquet.SourceGenerator.SampleAot.csproj
```

---

## 📚 Project Documentation

Detailed architecture specifications and design guidelines are available in the [`docs/`](docs/INDEX.md) folder:

- 📑 **[Vision & Architecture](docs/01-VISION-AND-ARCHITECTURE.md)**: Problem statement, reflection bottlenecks, performance goals, and memory expectations.
- 🛠️ **[API Design & Attributes](docs/02-API-DESIGN-AND-ATTRIBUTES.md)**: Public attribute definitions, configuration options, and generated code anatomy.
- ⚙️ **[Incremental Generator Pipeline](docs/03-INCREMENTAL-GENERATOR-PIPELINE.md)**: Roslyn `IIncrementalGenerator` architecture, equatable state models, and diagnostics.
- 🗺️ **[Roadmap & Contributing](docs/04-ROADMAP-AND-CONTRIBUTING.md)**: Development phases, testing strategy with snapshot testing, and benchmark suite setup.

---

## 📋 Task List & Backlog

See the [TODO.md](TODO.md) file for current task tracking and progress updates.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
