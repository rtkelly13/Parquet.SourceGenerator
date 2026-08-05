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
- **Native AOT support**, exercised on every CI run by publishing the AOT test project with
  `-r linux-x64` and executing the resulting native binary. `linux-x64` only, and note that
  Parquet.Net 6.0.3 emits its own trim (`IL2104`) and AOT-analysis (`IL3053`) warnings — so this
  covers the paths the test exercises rather than guaranteeing AOT safety in general.
- **`Guid` columns** written as native 16-byte values via pooled struct buffers rather than
  strings.
- **Parallel reader (`ReadParquetParallelAsync`)**: distributes object construction across row
  groups.
- **`IAsyncEnumerable<T>` streaming** directly into chunked row groups.
- **Microsecond `Int64` timestamps** via `[ParquetTimestamp(ParquetTimestampUnit.Microseconds)]`.
- **`ParquetSerializerOptions`** for `RowGroupSize`, `MaxDegreeOfParallelism` and
  `CompressionMethod` (`None`, `Snappy`, `Gzip`, `Lz4`, `Brotli`, `Zstd`), all applied.
- **Compiler diagnostics `PARQ001`–`PARQ005`**: partial-type enforcement, duplicate column names,
  no serializable members, ignored non-public members, and invalid decimal precision/scale.
- **CI workflow** building, testing and packing the solution. Benchmarks run on demand.

### Fixed before release
- `CompressionMethod` was accepted and discarded — no compression setting ever reached the writer.
- `[ParquetTimestamp(Microseconds)]` mapped to `DateTimeFormat.DateAndTime`, which Parquet.Net
  defines as *millisecond* precision, so microsecond columns were silently written coarser and the
  sub-millisecond component was lost. Now maps to `DateAndTimeMicros`.
- `ParquetTimestampUnit.Nanoseconds` and `ParquetSerializerOptions.UseMicrosecondTimestamps` are
  removed. Neither could work: Parquet.Net has no nanosecond format, and the schema is emitted at
  compile time so no runtime flag can change a column's encoding.

### Known gaps
- Nested collections are unsupported, and unsupported property types fail at runtime rather than
  producing a compile-time diagnostic.
- No performance measurements have been published.
