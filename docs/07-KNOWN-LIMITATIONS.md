# 07 - Known Limitations & Remediation Plan

An audit of the generator as it stands, recording what does not work, why, and what closing each
gap requires. Unlike the other documents in this folder — which describe intended design — this one
describes **observed behaviour**.

Findings come from reading `TargetParser`, `CodeEmitter` and the emitted code, and from inspecting
the published Parquet.Net packages on nuget.org. Items are marked ✅ once fixed, with the closing
commit's behaviour described inline.

**Status key**: 🔴 broken / emits wrong or uncompilable code · 🟠 silently does nothing ·
🟡 missing capability · ⚪ hygiene or documentation drift

---

## 1. Parquet.Net version support and .NET Framework

### 1.1 A `.V5` package would not restore net472 support 🟡

The assumption that a Parquet.Net 5.x-targeting variant retains .NET Framework support is
incorrect. TFMs as published:

| Parquet.Net | Ships `lib/` for | net472-capable |
|:---|:---|:---:|
| 6.0.3 | `net8.0`, `net10.0` | ✗ |
| 5.6.1 | `net8.0`, `net10.0` | ✗ |
| 4.25.0 | `netstandard2.0`, `netstandard2.1`, `net6.0`, `net8.0` | ✓ |
| 3.10.0 | `netstandard2.0`, `net5.0`, `net6.0` | ✓ |

**4.25.0 is the last net472-capable release.** Its dependency chain is netstandard2.0-clean
(IronCompress 1.5.2, Microsoft.Data.Analysis 0.21.1, Microsoft.IO.RecyclableMemoryStream 3.0.1),
and its nuspec carries an explicit `.NETStandard2.0` dependency group pulling
`System.Threading.Tasks.Extensions` — .NET Framework consumption is deliberate, not accidental.

### 1.2 The API break is between v5 and v6, not v4 and v5 🟡

Comparing the public API in each package's shipped `Parquet.xml`:

| Concern | v4.25.0 / v5.6.1 | v6.0.3 |
|:---|:---|:---|
| Column write | `WriteColumnAsync(DataColumn, …)` | `WriteAsync<T>(DataField, ReadOnlyMemory<T>, …)` |
| Column read | `ReadColumnAsync(DataField, …) → DataColumn` | `ReadAsync<T>(DataField, Memory<T>, …)` |
| Writer disposal | `Dispose` (v5 adds `DisposeAsync`) | `DisposeAsync` |
| Reader disposal | `Dispose` only | `DisposeAsync` |
| Compression | `ParquetWriter.CompressionMethod` property | `ParquetOptions.CompressionMethod` |

v4 and v5 are one API family; v6 introduced the `Memory<T>` buffer API this generator is built on.

The consequence: **a `.V4` backend is not a retarget, it is a second emitter.** The upside is that
one `DataColumn`-based backend covers v4 *and* v5, and the same work unlocks net472. Suggested
naming is `Parquet.SourceGenerator.V4` (or `.Classic`) rather than `.V5`.

Emitter changes required for a classic backend:

- `WriteAsync<T>(field, memory)` → `WriteColumnAsync(new DataColumn(field, array))`
- `ReadAsync<T>(field, memory)` → `ReadColumnAsync(field)`, then read `.Data` / `.DefinedData`
- `await using` → `using` (no `DisposeAsync` on the v4 reader)
- Compression moves from `ParquetOptions` onto the writer

The ArrayPool zero-allocation story does not survive this — `DataColumn` allocates its own arrays —
so the classic package should not inherit the main package's performance claims.

### 1.3 net472 constraints beyond Parquet.Net itself 🟡

Three issues independent of which Parquet.Net version is chosen:

- The generated code uses `IAsyncEnumerable<T>` and `[EnumeratorCancellation]`, requiring
  `Microsoft.Bcl.AsyncInterfaces` and `System.Memory` on net472 — or those APIs must be `#if`'d out
  of the net472 emit.
- **IronCompress ships no `win-x86` native binary** (only `linux-x64`, `linux-arm64`, `osx-arm64`,
  `win-x64`). 32-bit .NET Framework applications fail at runtime on any compressed write. `runtimes/`
  RID assets also do not resolve at all under `packages.config`.
- `IsExternalInit` is `internal` in `Parquet.SourceGenerator.Attributes`, so net472 consumers using
  `init` accessors need their own copy.

---

## 2. Correctness

### 2.1 `ReadParquetParallelAsync` performs no parallel work 🔴

`CodeEmitter.EmitReadParallelAsync` computes `targetParallelism` and never reads it again. The
emitted body is a sequential `for (int r = 0; r < rgCount; r++)` loop; the emitter contains no
`Task.Run`, `Parallel.For` or `Task.WhenAll` anywhere. Both the `maxDegreeOfParallelism` parameter
and `ParquetSerializerOptions.MaxDegreeOfParallelism` are inert.

The README ("Multi-core parallel read across row groups"), CHANGELOG ("distributes object
construction across row groups") and the generated XML doc comment all claim otherwise.

Either implement genuine parallelism or withdraw the claim and the parameter. Note that the
sequential form appears to be a deliberate fix — a single `ParquetReader` over one `Stream` cannot
be read concurrently — so real parallelism means decoupling column decode (sequential, stream-bound)
from object materialisation (parallelisable).

### 2.2 The read path reallocates its result list on every row group ✅

`EmitReadAsync` constructs `results` with capacity equal to the *total* row count, then emits
`results.Capacity = results.Count + rowCount;` inside the row-group loop. `List<T>.Capacity`
reallocates whenever the assigned value differs from the current backing array length, so each row
group allocates a smaller array and copies into it — repeatedly — before the list grows back.

Single-row-group files are unaffected. Multi-row-group files (exactly what `WriteParquetBatchedAsync`
produces) pay O(groups × rows) of copying. This is the most likely cause of the published benchmark
showing reads at 1.25× the reflection baseline's time with 2.36× its allocations.

### 2.3 Positional records emit uncompilable code 🔴

The emitter always materialises via object initializer (`new {ClassName} { Prop = … }`). A positional
record synthesises a primary constructor and a copy constructor but **no parameterless constructor**,
so the emitted code fails with CS7036.

`[ParquetSerializable] public partial record Person(int Id, string Name);` — the shape used as the
example in `04-ROADMAP-AND-CONTRIBUTING.md` — is affected. No test covers a positional record; every
test model declares property bodies.

Fix requires either constructor-based materialisation when a primary constructor is present, or a
diagnostic rejecting the shape.

### 2.4 Get-only properties and readonly fields emit uncompilable code 🔴

`TargetParser` performs no settability check — there is no inspection of `SetMethod`, `IsReadOnly` or
`IsRequired`. A `public int Id { get; }` is collected and then assigned in an object initializer,
producing CS0200 inside generated code with no diagnostic pointing at the cause.

### 2.5 Inherited members are silently dropped 🔴

`TargetParser` uses `typeSymbol.GetMembers()`, which returns declared members only. A
`[ParquetSerializable]` type deriving from a base that carries columns loses every inherited member,
with no warning. Walking `BaseType` (excluding `System.Object`, handling `new`/`override` shadowing)
is required.

### 2.6 Nested and generic types are unhandled 🔴

`ClassName` is `typeSymbol.Name`, so:

- A **nested type** emits a namespace-level extension class referencing an unqualified name that
  cannot resolve from that scope.
- A **generic type** emits `Foo` without its type parameters.
- The `AddSource` hint name is namespace + class name only, so two same-named nested types under one
  namespace collide — the same class of failure the namespace-qualification fix addressed.

Each needs either correct emission or a diagnostic.

### 2.7 `DateTimeOffset` is claimed but does not round-trip 🔴

`TargetParser.ClassifyKind` maps `System.DateTimeOffset` to `PropertyKind.DateTime`, which emits a
`DateTimeDataField` whose CLR type is `DateTime`, while the write call is `WriteAsync<DateTimeOffset>`
over `ReadOnlyMemory<DateTimeOffset>`. The types disagree, and the offset would be lost even if the
call bound. There is no test coverage for `DateTimeOffset` anywhere in the repository.

Either convert explicitly at the buffer boundary (documenting that the offset is normalised to UTC)
or remove the mapping so it reports as unsupported.

### 2.8 Unsupported property types fail at runtime, not compile time 🟡

`ClassifyKind` funnels everything unrecognised into `PropertyKind.Primitive` and emits
`typeof(X)` for Parquet.Net to reject at runtime. Affected: `List<T>` and other collections, nested
POCOs, `char`, `sbyte`, `ushort`, `uint`, `ulong`, `DateOnly`, `TimeOnly`, `BigInteger`.

This is the largest developer-experience gap. A `PARQ006` diagnostic driven by an explicit whitelist
converts a confusing runtime exception into a squiggle on the offending property — and is a
prerequisite for the classic backend, whose supported type set is narrower still.

### 2.9 Every reference-type column is forced nullable 🟠

`TargetParser` computes `isNullable = memberType.IsReferenceType || NullableAnnotation == Annotated`.
A non-nullable `string Name` under `#nullable enable` therefore emits `isNullable: true`, so the
required/optional distinction is lost in the written schema. Downstream consumers (Spark, Athena,
PyArrow) do read that distinction.

The annotation is already available; the `IsReferenceType` disjunct should be dropped when the
compilation has nullable analysis enabled.

---

## 3. Options and API surface

### 3.1 `ParquetSerializerOptions` does nothing on any read path 🟠

`EmitReadAsync`, `EmitReadParallelAsync` and `EmitReadStreamAsync` each emit
`options ??= …Default;` and then never reference `options` again — `ParquetReader.CreateAsync` is
called without it. Every setting silently no-ops on read.

### 3.2 Row-group sizing uses a magic-number sentinel 🟠

`options.RowGroupSize > 0 && options.RowGroupSize != 50_000 ? options.RowGroupSize : rowGroupSize`

Setting `RowGroupSize = 50_000` explicitly is indistinguishable from leaving it at its default, and
the method parameter silently wins. A nullable `int?` default expresses "unset" without a sentinel.
`MaxDegreeOfParallelism` uses the same `> 0` pattern.

### 3.3 `ParquetSerializerOptions.Default` is a mutable shared singleton 🟠

`public static ParquetSerializerOptions Default { get; } = new();` exposes `{ get; set; }`
properties, so any consumer can mutate process-global defaults for every serializer in the
application. Init-only properties, or returning a fresh instance per access, closes this.

### 3.4 `SchemaName` is a dead public API 🟠

`ParquetSerializableAttribute.SchemaName` is public and documented, is parsed by `TargetParser`, is
carried on `TargetClassModel` — and is never read by `CodeEmitter`. `TargetClassModel.IsRecord` and
`IsValueType` are likewise captured and unused.

Either wire `SchemaName` into the emitted schema or remove it before it reaches a stable release.

### 3.5 Memory overloads are asymmetric, and one leaks ⚪

`ReadParquetAsync(ReadOnlyMemory<byte>)` constructs a `MemoryStream` it never disposes (CA2000).
It is also the only memory overload — `ReadParquetParallelAsync` and `ReadParquetStreamAsync` have
no equivalent.

### 3.6 `[ParquetColumn]` cannot express order without a name 🟡

The attribute has only a name-taking constructor, so `[ParquetColumn(Order = 2)]` — reordering a
column without renaming it — is impossible. A parameterless constructor with a nullable `Name`
resolves this.

### 3.7 `ParquetOptions.CompressionLevel` is not exposed 🟡

`ParquetSerializerOptions` surfaces `CompressionMethod` but not the compression level Parquet.Net
accepts alongside it.

---

## 4. Documentation drift ⚪

- `CHANGELOG.md` states "Nothing has been released yet"; **0.0.1 is published on nuget.org**
  (alongside `0.0.1-dev.1` and `0.0.1-dev.2`).
- `CHANGELOG.md` lists `MaxDegreeOfParallelism` among options that are "all applied" (see 2.1).
- `README.md` says "Not benchmarked" three lines above an embedded benchmark table.
- `04-ROADMAP-AND-CONTRIBUTING.md` leaves phases 2–5 entirely unticked despite being shipped, and
  still lists "Update Parquet.Net to 4.x / 5.x" as pending while the repo targets 6.0.3.
- `release.yml`'s package-layout check verifies `netstandard2.0;netstandard2.1;net8.0` for the
  Attributes package, but the csproj also targets `net9.0`.
- `INDEX.md` says "Native AOT … Not yet verified — CI does not run an AOT publish", but `ci.yml`
  publishes and executes the AOT test binary on every run.

---

## 5. Remediation order

Sequenced so that each step is independently shippable and the cheap high-impact fixes land first.

| # | Item | Change |
|:---|:---|:---|
| 1 | 2.2 | ✅ Removed the in-loop `Capacity` assignment |
| 2 | 3.1, 3.2, 3.3 | Thread options through reads; drop sentinels; freeze `Default` |
| 3 | 2.1 | Implement real parallelism, or withdraw the claim and the parameter |
| 4 | 2.8 | `PARQ006` unsupported-type diagnostic from an explicit whitelist |
| 5 | 2.3, 2.4 | `PARQ007` unsettable member / positional-record handling |
| 6 | 2.6 | `PARQ008` nested and generic type rejection |
| 7 | 2.5 | Walk base types for inherited members |
| 8 | 2.7, 2.9 | `DateTimeOffset` conversion; honour nullable annotations |
| 9 | 3.4, 3.5, 3.6, 3.7 | `SchemaName`, memory overloads, attribute and options surface |
| 10 | 4 | Reconcile documentation with behaviour |
| 11 | 1.2 | `Parquet.SourceGenerator.V4` classic backend + net472 target |

Items 4–6 are prerequisites for 11: the classic backend supports a narrower type set, so
compile-time rejection must exist before a second backend can report it.
