# Changelog

All notable changes to **Parquet.SourceGenerator** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

Changes since `0.0.2`; this section becomes the next release entry when one is cut.

---

## [0.0.2] - 2026-09-11

Changes since `0.0.1`, which is published on nuget.org alongside the `0.0.1-dev.1` and
`0.0.1-dev.2` prereleases.

This release introduces the generated read builder (`{T}Parquet.From(...)`) and keeps every
existing flat read method working as a forwarder. Both surfaces ship together **deliberately and
for this release only**: `docs/19-PUBLIC-API-SURFACE.md` decision D3 removes the flat methods at
the `0.1.0` freeze. Callers should adopt the builder now; the flat methods are not yet marked
`[Obsolete]` because the two surfaces are still being validated against each other.

### Added
- **Generated read builder (`{T}Parquet.From(...)`)**: reads are now expressed as a chain rather
  than a cross-product of method names — `PersonParquet.From(stream).ToListAsync(ct)`,
  `PersonParquet.From(bytes).Parallel().ToArrayAsync(ct)`,
  `PersonParquet.From(bytes).Where(m => m.Id.Min >= 100).ToListAsync(ct)`. The builder is
  **type-state**: a combination that cannot work is not a member you can call. `Parallel()` is
  absent on a `Stream` source, because a single `ParquetReader` seeks within its stream and
  concurrent row-group reads corrupt each other; `Where()` and `Parallel()` are mutually absent
  until a parallel reader accepts a predicate. `Where` also closes a gap in the flat methods,
  where pushdown existed on the `Stream` overloads but not the buffer ones. Rationale and the
  full axis grid are in `docs/19-PUBLIC-API-SURFACE.md` (decision D2).
- **API change contract (`PARQAPI001` / `PARQAPI002`)**: three API surfaces are now governed, and
  nothing enters one without a catalogue line *and* a `docs/api/LEDGER.md` entry recording its
  semver bucket. The emitted consumer API is gated at **build** time against the `*.api.txt`
  baselines added by #215 (`PARQAPI001`); internal seams — members widened past `private` for
  cross-component reuse — are catalogued in the new `src/api/seams.txt` and gated by `PARQAPI002`;
  the shipped package API keeps its existing `RS0016` gate unchanged. Body, performance and comment
  changes alter the golden `.g.cs` but not the `.api.txt`, and do not trip anything. A
  `**Unapproved-by-design:**` ledger entry suppresses the build error for spikes and is rejected by
  CI on `main`. Both gates are analyzers in `tools/Parquet.SourceGenerator.ApiGates`, are never
  packed, and short-circuit unless handed their catalogue as an `AdditionalFile`, so they cannot
  run in a consumer's compilation. See `docs/18-API-CHANGE-CONTRACT.md`.
- **Direct columnar handoff (write)**: flat `[ParquetSerializable]` models now also emit a
  `{Type}ColumnarBatch` struct plus `WriteParquetRowGroupAsync(batch)`,
  `WriteParquetRowGroupColumnarAsync(rowCount, ...)` and a stream-level `batch.WriteParquetAsync`.
  A caller whose data is already in contiguous column buffers skips the row-to-column transpose and
  its `ArrayPool` rentals entirely — buffers reach Parquet.Net verbatim. Nullable value columns take
  packed values plus explicit definition levels. Measured at ~7% of end-to-end write time on a
  16-column schema, identical in Workstation and Server GC, with allocation unchanged; see
  `docs/12-BUFFER-REUSE-AND-EXTRACTION-STRATEGIES.md` §6. Models with struct, list or map members
  are unaffected and keep the row-oriented API only.
- **Apache Arrow `RecordBatch` ingestion (experimental, #177)**: when — and only when — the consumer
  compilation references Apache.Arrow, the generator emits an extra `{Namespace}.{Type}.Arrow.g.cs`
  per Arrow-representable `[ParquetSerializable]` type, adding
  `WriteParquetRowGroupAsync(ParquetWriter, RecordBatch, ParquetSerializerOptions?, CancellationToken)`
  to the same partial class. Neither shipped package takes an Apache.Arrow dependency; the gate is a
  single `bool` off `CompilationProvider`, so toggling the reference re-runs only the Arrow-gated
  output. Columns are matched by name and validated strictly (every offending field reported at
  once); fixed-width Arrow buffers are handed to the writer with no copy and no `ArrayPool` rental;
  nullable columns derive definition levels from the Arrow validity bitmap and produce byte-identical
  files to the POCO write path. Supported Apache.Arrow floor: `23.0.0`.
- **Roslyn incremental source generator**: compiles zero-reflection Parquet serializers and
  deserializers against Parquet.Net low-level primitives.
- **Native AOT support**, exercised on every CI run by publishing the AOT test project with
  `-r linux-x64` and executing the resulting native binary. `linux-x64` only, and note that
  Parquet.Net 6.0.3 emits its own trim (`IL2104`) and AOT-analysis (`IL3053`) warnings — so this
  covers the paths the test exercises rather than guaranteeing AOT safety in general.
- **`Guid` columns** written as native 16-byte values via pooled struct buffers rather than
  strings.
- **Parallel reader (`ReadParquetParallelAsync`)**: materialises into a single pre-sized array
  indexed by row-group offset. Over a `ReadOnlyMemory<byte>` it decodes row groups concurrently —
  one `ParquetReader` over one stream per worker, groups claimed dynamically — with results in file
  order. Over an arbitrary `Stream` it stays sequential, because a stream cannot be shared between
  readers, and `maxDegreeOfParallelism` is not honoured there.
- **`Parquet.SourceGenerator.V5`**: a second generator emitting against the Parquet.Net 4.x/5.x
  `DataColumn` API, which is what restores .NET Framework 4.7.2 support. It accepts a narrower set
  of member types than the v6 backend and reports the difference as `PARQ011`.
- **`IAsyncEnumerable<T>` streaming** directly into chunked row groups.
- **Microsecond `Int64` timestamps** via `[ParquetTimestamp(ParquetTimestampUnit.Microseconds)]`.
- **`ParquetSerializerOptions`** for `RowGroupSize`, `CompressionMethod` (`None`, `Snappy`, `Gzip`,
  `Lz4`, `Brotli`, `Zstd`) and `CompressionLevel` (`Optimal`, `Fastest`, `NoCompression`,
  `SmallestSize`; unset keeps Parquet.Net's default). `MaxDegreeOfParallelism` supplies the worker
  count for the buffer-based parallel read.
- **Compiler diagnostics `PARQ001`–`PARQ011`**: partial-type enforcement, duplicate column names,
  no serializable members, ignored non-public members, invalid decimal precision/scale, unsupported
  member types, unassignable members, types with no parameterless constructor, nested or generic
  target types, and member types the 4.x/5.x backend cannot represent.
- **CI workflow** building, testing and packing the solution. Benchmarks run on demand.
- **Sorted row-group pruning (experiment, issue #151)**: mark a column `[ParquetSortKey]` and the
  generator emits `ReadParquetBy<Column>Async` (point lookup) and `ReadParquet<Column>RangeAsync`
  (inclusive slice) for it. Both certify the column as sorted from the footer `[Min, Max]`
  statistics and binary search that metadata, so only the row groups that can contain the key are
  decompressed; overlapping or missing statistics fall back to a full scan and the answer is
  identical either way. Pass a `ParquetPruneStatistics` to see how many row groups were skipped.
  The marker is opt-in: a model with no `[ParquetSortKey]` emits exactly what it did before, with
  none of the lookup API. Marking a member the rules cannot support — nullable, `string`, a
  compound member, or a type with no Parquet statistics order — is reported as **PARQ014** with the
  reason rather than silently emitting nothing.
  Note the cost shape: certifying sortedness reads every row group's statistics, so the metadata
  phase is O(N) in row groups. Only decompression is logarithmic — that is where the speedup
  comes from, and it is why the win grows with row-group payload size rather than with row-group
  count alone.
- **Span-keyed string deduplication** (`DeduplicateStrings = true`): string columns are read
  through Parquet.Net's raw `ReadOnlyMemory<char>` surface and interned against a pooled
  open-addressed table keyed on `ReadOnlySpan<char>`, so a repeated value costs no `string`
  allocation at all. Hash matches are always confirmed with a full ordinal comparison. On the
  Adult Census dataset (32,561 rows, 9 categorical columns) this cuts managed read allocation
  from 20.38 MB to 8.75 MB. The `ReadOnlySpan<byte>` variant the design originally called for is
  not reachable — Parquet.Net 6.1.0 exposes no UTF-8 byte surface for string columns; see
  `UPSTREAM_DEPENDENCY_LIMITATIONS.md`.

### Changed
- **Breaking (pre-1.0), #218 — every configuration option now has a single home.** The duplicated
  positional parameters are removed from the generated API; `ParquetSerializerOptions` is the only
  place either setting lives:
  - `maxDegreeOfParallelism` is gone from `ReadParquetParallelAsync` and
    `ReadParquetParallelArrayAsync` (both the `Stream` and the `ReadOnlyMemory<byte>` overload).
    Set `ParquetSerializerOptions.MaxDegreeOfParallelism` instead.
  - `rowGroupSize` is gone from `WriteParquetBatchedAsync` and from the `IAsyncEnumerable<T>`
    overload of `WriteParquetAsync`, on both the modern and the classic emitter. Set
    `ParquetSerializerOptions.RowGroupSize` instead.

  Migration is mechanical: `WriteParquetBatchedAsync(stream, rowGroupSize: 10_000)` becomes
  `WriteParquetBatchedAsync(stream, new ParquetSerializerOptions { RowGroupSize = 10_000 })`. Both
  parameters sat behind another optional parameter and so were already passed by name, and both
  previously took precedence over the options property — silently, since the signature said nothing
  about it. One behaviour change beyond the removal: a non-positive `maxDegreeOfParallelism` used to
  be discarded without error, so a caller who passed `0` got the options value or
  `Environment.ProcessorCount`; there is now no argument to discard. The rule this applies is
  recorded in `docs/19-PUBLIC-API-SURFACE.md`. `0.0.x` permits the break; the decision is recorded
  as `breaking-major` in `docs/api/LEDGER.md`.
- **Formatting tooling consolidated on CSharpier.** `dotnet format whitespace` is removed from CI:
  its Roslyn formatter disagrees with CSharpier on layout (case-body and pattern-arm indentation),
  and it policed nothing beyond `.cs` files anyway. `.editorconfig` now carries CSharpier's
  configuration (`max_line_length`, its non-configurable behaviors) and disables `IDE0055`, so no
  Roslyn-based formatter — build, IDE or CLI — competes with CSharpier over C# layout. `.gitattributes`
  pins `eol=lf` on checkout, replacing the whitespace formatter's line-ending role for non-C# files.

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
- `ReadParquetParallelAsync(Stream)` reads row groups sequentially — a single `ParquetReader`
  seeks within its stream, so concurrent row-group reads would corrupt each other. It silently
  ignores `ParquetSerializerOptions.MaxDegreeOfParallelism`; pass a `ReadOnlyMemory<byte>` for the
  genuine parallel path. On the new builder this combination is simply unwriteable — `Parallel()`
  does not exist on a `Stream` source — so the flat method is the only place the lie remains.
- Nested and generic target types are rejected (`PARQ009`/`PARQ010`) rather than supported.
- .NET Framework needs the `Parquet.SourceGenerator.V5` package. The classic backend has no
  `ArrayPool` story — `DataColumn` allocates its own arrays — so it does not inherit the main
  package's allocation characteristics, and it offers no streaming or parallel reader.
- IronCompress ships no `win-x86` native binary, so 32-bit .NET Framework applications fail at
  runtime on any compressed write.
- See [docs/07-KNOWN-LIMITATIONS.md](docs/07-KNOWN-LIMITATIONS.md) for the full audit.
