# Changelog

All notable changes to **Parquet.SourceGenerator** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

Nothing has been released yet. There is no tag and no published package; this section becomes the
first release entry when one is cut.

### Added
- **Roslyn incremental source generator**: compiles zero-reflection Parquet serializers and
  deserializers against Parquet.Net low-level primitives.
- **Reflection-free generated code**, a precondition for Native AOT. Not yet verified against the
  AOT toolchain in CI.
- **`Guid` columns** written as native 16-byte values via pooled struct buffers rather than
  strings.
- **Parallel reader (`ReadParquetParallelAsync`)**: distributes object construction across row
  groups.
- **`IAsyncEnumerable<T>` streaming** directly into chunked row groups.
- **Microsecond `Int64` timestamps** via `[ParquetTimestamp(ParquetTimestampUnit.Microseconds)]`.
- **`ParquetSerializerOptions`** for `RowGroupSize` and `MaxDegreeOfParallelism`.
  `CompressionMethod` is present on the type but not yet applied.
- **Compiler diagnostics `PARQ001`–`PARQ005`**: partial-type enforcement, duplicate column names,
  no serializable members, ignored non-public members, and invalid decimal precision/scale.
- **CI workflow** building, testing and packing the solution. Benchmarks run on demand.

### Known gaps
- `ParquetSerializerOptions.CompressionMethod` is not applied.
- `ParquetTimestampUnit.Nanoseconds` is declared but not implemented.
- Nested collections are unsupported, and unsupported property types fail at runtime rather than
  producing a compile-time diagnostic.
- No performance measurements have been published.
