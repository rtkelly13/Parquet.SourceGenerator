# Changelog

All notable changes to **Parquet.SourceGenerator** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.0.1] - 2026-08-03

### Added
- **Roslyn Incremental Source Generator**: Statically compiles zero-reflection Parquet serializers and deserializers targeting Parquet.Net v6 low-level primitives.
- **Native AOT Compatibility**: 100% reflection-free execution safe for `.NET 8` Native AOT (`PublishAot=true`).
- **Native 16-byte `Guid` Binary Encoding**: Direct `ArrayPool<Guid>` struct column buffer transfer (**1.58x faster**, **-39.1% memory**).
- **Multi-Core Parallel Reader (`ReadParquetParallelAsync`)**: Multi-threaded object creation across multi-row-group Parquet files (**1.45x faster**).
- **`IAsyncEnumerable<T>` Streaming**: Asynchronously stream items directly into chunked Parquet row groups.
- **Compact 8-Byte Int64 Timestamps**: Support for `[ParquetTimestamp(ParquetTimestampUnit.Microseconds)]` (**33% size reduction**).
- **Configurable `ParquetSerializerOptions`**: Centralized configuration for `RowGroupSize`, `MaxDegreeOfParallelism`, `CompressionMethod`, and timestamps.
- **Compiler Diagnostic Rules (`PARQ001` - `PARQ003`)**: Compile-time validation for partial types, duplicate column names, and missing properties.
- **Automated GitHub Actions Guardrails**: Continuous Integration and BenchmarkDotNet baseline tracking workflows.
