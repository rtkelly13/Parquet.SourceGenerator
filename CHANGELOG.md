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
- **Array-backed reader (`ReadParquetParallelAsync`)**: materialises into a single pre-sized array
  indexed by row-group offset. Reads row groups sequentially; `maxDegreeOfParallelism` is accepted
  but not yet honoured.
- **`IAsyncEnumerable<T>` streaming** directly into chunked row groups.
- **Microsecond `Int64` timestamps** via `[ParquetTimestamp(ParquetTimestampUnit.Microseconds)]`.
- **`ParquetSerializerOptions`** for `RowGroupSize` and `CompressionMethod` (`None`, `Snappy`,
  `Gzip`, `Lz4`, `Brotli`, `Zstd`). `MaxDegreeOfParallelism` is present but inert — see
  `ReadParquetParallelAsync` above.
- **Compiler diagnostics `PARQ001`–`PARQ008`**: partial-type enforcement, duplicate column names,
  no serializable members, ignored non-public members, invalid decimal precision/scale, unsupported
  member types, unassignable members, and types with no parameterless constructor.
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
- Nested collections, `DateTimeOffset` and positional records are unsupported. They are now
  rejected at compile time (`PARQ006`/`PARQ008`) rather than failing at runtime.
- `ReadParquetParallelAsync` reads row groups sequentially; `maxDegreeOfParallelism` is inert.
- Inherited members are not collected, and nested or generic target types are not handled.
- .NET Framework is unsupported — Parquet.Net 5 and 6 ship `net8.0`/`net10.0` only.
- See [docs/07-KNOWN-LIMITATIONS.md](docs/07-KNOWN-LIMITATIONS.md) for the full audit.
