# 22 - Generated Code Metrics

> Layer 2 of [#251](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/251). This page
> covers the **generated** code — the `*.g.cs` this repository emits into consumers' compilations.
> The hand-written code under `src/` is [21](./21-CODE-METRICS.md) (layer 1); duplication (layer 3)
> and mutation testing (layer 4) are separate work.

## The question this answers

**Is the code we generate getting harder to maintain, and is it growing faster than it grows more
capable?**

Nothing could answer that before. The generated code is the code users actually run and actually
debug through, it exists in no shipped assembly, and no off-the-shelf .NET quality tool can see it.
`*.api.txt` ([17](./17-GENERATED-API-BASELINES.md)) made the emitted *surface* visible; this makes
the emitted *volume and shape* visible, and puts the two in the same file so the ratio between them
can be watched.

## Where the numbers live

Beside the golden files, one baseline per model:

```
test/Parquet.SourceGenerator.Tests/GoldenFiles/
  OrderEventParquetExtensions.g.cs          <- the emitted source (golden-gated)
  OrderEventParquetExtensions.api.txt       <- its public surface  (docs/17)
  OrderEventParquetExtensions.metrics.txt   <- its metrics         (this page)
  Models/OrderEvent.cs                      <- the consumer-side declaration it extends
```

**Per model, not one aggregate file.** The `.api.txt` convention is already per model, a reviewer
already reads these three files as a set, and the diff for a change to one model stays inside that
model's file instead of rewriting a shared table. The cross-model view that an aggregate would have
given is produced on demand into the CI job summary by
`dotnet run scripts/CodeMetrics.cs -- --summary <file>`.

### The format

Deliberately the same grammar as layer 1 — one self-contained line per entity, ordinal-sorted,
fully-qualified identity with parameter types but not parameter names — so a reader learns one
grammar for both artefacts. See [21](./21-CODE-METRICS.md#a-normalised-rendering-not-the-tools-xml)
for why a flat text rendering rather than the tool's XML.

```
S:OrderEventParquetExtensions | EMITTER=v6 MEMBERS=82 CC=310 CL=82 SLOC=2735 ELOC=890 ERRORS=0 METHODS=81 MAXCC=26 ELOC_PER_MEMBER=10.9
T:SampleDomain.Models.OrderEventParquetExtensions | MI=45 CC=265 CL=76 DIT=1 SLOC=2451 ELOC=835
M:SampleDomain.Models.OrderEventParquetExtensions.ResolveSchemaField(...) | MI=56 CC=8 CL=7 DIT=- SLOC=52 ELOC=14
```

`S:` is the one line layer 1 does not have: the per-model summary that answers the question above.
Assembly and namespace rows are omitted, because the compilation also contains the model
declaration and a namespace total would not be a number about emitted code.

| Field | Meaning |
|:--|:--|
| `EMITTER` | `v6` (`Parquet.SourceGenerator`) or `V5` (`Parquet.SourceGenerator.Legacy`) |
| `MEMBERS` | Emitted public members — **read from the sibling `.api.txt`**, never recomputed |
| `CC` `CL` `SLOC` `ELOC` | Totals over the types the golden file declares |
| `ERRORS` | C# compile errors in the emitted code (see [below](#errors-is-a-metric-not-an-exception)) |
| `METHODS` / `MAXCC` | Emitted method count and the worst single method's cyclomatic complexity |
| `ELOC_PER_MEMBER` | The size-per-capability ratio |

## The size-per-capability ratio

`ELOC_PER_MEMBER` = emitted **executable** lines ÷ emitted **public members**.

- **The numerator is ELOC, not SLOC.** Source lines include the emitted doc comments, blank lines
  and the inactive halves of `#if NET6_0_OR_GREATER` — volume a consumer never executes and never
  steps through. Executable lines are the code that is actually JITted and actually debugged, which
  is the cost this ratio is meant to price.
- **The denominator is the `.api.txt` member count**, read from the file rather than recomputed.
  That count is already this repository's definition of emitted surface, already governed by the
  API change contract ([18](./18-API-CHANGE-CONTRACT.md)), and #244 established that this
  repository cannot afford two definitions of it. `GeneratedCodeMetricsBaselineTests` asserts the
  number on the summary line is the one `GeneratedApiBaseline.CountMembers` gives.
- **Reading it.** Rising means each unit of capability now costs more emitted machinery — the
  generator is getting heavier without getting more useful. Falling means the emitter gained
  leverage. It is the number to quote when arguing that a feature "paid for itself".

**Its honest weakness:** member count is a crude proxy for capability. #244 already found that
member count alone cannot measure surface *shrinkage*, and a single well-chosen method can be worth
ten narrow overloads. The ratio is a trend line to be argued with, not a score to optimise — the
same posture [21](./21-CODE-METRICS.md) takes towards the Maintainability Index.

## Which metrics carry signal for generated code — measured, not assumed

Generated code is not written to be read. A long flat emitted method with no branches is fine,
where the same shape hand-written would be a smell. So layer 1's metric set was re-examined against
the 264 emitted methods and 35 emitted types in the five golden models rather than imported.

| Metric | Verdict | Evidence |
|:--|:--|:--|
| `CC` cyclomatic complexity | **Gated.** The strongest signal. | Median 2, p90 15, max 97. 49 of 264 methods exceed 7 and 9 exceed 25. Real spread, and it tracks something a consumer feels: the number of paths through the code they are stepping into. |
| `SLOC` / `ELOC` | **Gated.** | The volume the emitter imposes on every consuming compilation; the input to the ratio above. |
| `CL` class coupling | **Gated.** | Median 7 per method, max 32; types range 1–76. It measures how much of the Parquet.Net surface the emitted code binds to, which is exactly the thing that breaks on a dependency upgrade. |
| `ERRORS` | **Gated.** | Does the emitted code compile. Zero spread is the point. |
| `MEMBERS` | **Gated.** | Cross-reference with `.api.txt`; a mismatch means the two artefacts were refreshed apart. |
| `DIT` depth of inheritance | **Gated, but carries no signal today.** | Constant 1 across every emitted type — the emitter emits no hierarchies. Gating a constant costs nothing and a change would be genuinely notable, so it stays; nobody should read anything into it. |
| `MI` maintainability index | **Reported, NOT gated.** | See below. |

### Why the Maintainability Index is reported but not gated

[21](./21-CODE-METRICS.md) already notes that MI is a poor absolute judgement of quality and keeps
it only as a *change detector* for hand-written code. For generated code even that justification
fails, on two measurements:

1. **It is a restatement of method length.** Across the 264 emitted methods, MI correlates with
   `ln(SLOC)` at **r = −0.95**; with cyclomatic complexity only at −0.78. Gating MI on emitted code
   would therefore fire almost exclusively when `SLOC` — already gated, exactly — had already
   fired. It is a second alarm wired to the same sensor, with a ±2 fudge factor attached.
2. **It saturates exactly where it would matter most.** `ListOrderParquetExtensions.WriteParquetRowGroupAsync`
   is 1,148 source lines and scores **MI 0**. The index has bottomed out: no further degradation of
   that method can move it. A change detector that cannot detect change on the worst entity in the
   set is not a change detector.

And the underlying premise does not transfer. MI penalises length because a long *hand-written*
method is evidence of a human failing to decompose. A long emitted method is a deliberate choice by
the emitter — inlining a per-column ladder rather than emitting a loop is the whole performance
argument ([11](./11-PERFORMANCE-OPTIMIZATION-FINDINGS.md)). Gating MI here would gate the design.

So MI is written into the baseline for the reader, and the gate ignores it. It is the one number in
the file that may be stale between refreshes; that is the deliberate cost of keeping one grammar.

## `ERRORS` is a metric, not an exception

Layer 1 **fails rather than reports** when the compilation it measures has an error, because an
unresolved reference in `src/` means a broken measurement host. Layer 2 deliberately does the
opposite: emitted code that does not compile is a defect in the emitter, not a broken host, and
throwing would hide it behind a stack trace instead of recording it in a file a reviewer reads.

So the count is a gated field on the summary line. `ERRORS=0` is the expectation. A non-zero
baseline is a recorded, reviewable defect that a reviewer can see the size of, and the gate stops
it getting any bigger.

Today one model is non-zero: **`NestedOrderParquetExtensions` at `ERRORS=6`**. The emitted writer
dereferences a nullable value-type compound member directly —

```csharp
var ca_7_0 = item.Start;   // Start is Point?
var cv_7 = ca_7_0.X;       // CS1061: 'Point?' has no definition for 'X'
```

— in both the `NET6_0_OR_GREATER` and legacy write paths, and the emitted definition level is
hard-coded to "present" besides. Tracked as
[#255](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/255); layer 2 measures it rather
than fixing it, and the gate stops the count growing.

That this was undiscovered until now is itself a finding: `GoldenCodeGenRegressionTests` parses the
emitted source and asserts zero **syntax** diagnostics, but never binds it against Parquet.Net, so
a semantic error in emitted code could not fail any gate. It can now.

## How the emitted code is compiled in order to be measured

Generated code is a *fragment*: it extends a type the consumer wrote. On its own it does not
compile, and metrics over a compilation with unresolved types are fiction — `IOperation` trees
degrade and the Halstead counts MI and coupling are built from become meaningless.

So each golden file is compiled together with the model declaration it was generated for, checked
in at `GoldenFiles/Models/<Model>.cs`. Those files mirror the models
`GoldenCodeGenRegressionTests` generates from; they are inputs to the measurement, not artefacts of
it, and they are not compiled into the test assembly (`GoldenFiles/**/*.cs` is `Compile`-removed).
They cannot drift silently: if the emitter starts referencing a member the declaration does not
have, `ERRORS` moves and the gate fires.

Two more decisions worth knowing:

- **References come from the project the golden files live in**, not from a second set of package
  pins — Parquet.Net 6.1.0 and Apache.Arrow 23.0.0, exactly what a consumer of this repository's
  tests gets. The one exception is the legacy golden file: the V5 emitter targets the classic
  `DataColumn` API and its output does not compile against Parquet.Net 6 **at all**, so it is
  measured against **4.25.0**, the same pin `test/PackageConsumptionLegacy` uses.
- **Parse options come from that project too**, so the preprocessor symbols are a modern consumer's
  (`net8.0`). That matters: the emitted code is full of `#if NET6_0_OR_GREATER`, and measuring the
  branch a real consumer does not compile would be measuring the wrong code. The inactive branch
  still counts towards `SLOC` — it is text in the method — but contributes no complexity, which is
  the honest answer for code that is not there.

## Determinism

The same contract as [17](./17-GENERATED-API-BASELINES.md) and [21](./21-CODE-METRICS.md), and the
same computation: `Microsoft.CodeAnalysis.CodeMetrics.CodeAnalysisMetricData`, on a Roslyn version
pinned in `scripts/CodeMetrics.cs`'s own `#:package` directives rather than taken from the SDK.
Every ordering is `StringComparer.Ordinal`. `ELOC_PER_MEMBER` is a quotient of two exact integers
rendered to one decimal place with `CultureInfo.InvariantCulture`.

Verified by regenerating twice (byte-identical) and once under `LC_ALL=tr_TR.UTF-8`
(byte-identical). Cross-platform determinism is re-proven on every CI run, which regenerates these
baselines on `ubuntu-latest` / x64 from files generated on macOS / arm64 and compares them with no
tolerance at all.

## The tolerance, and the gate

| Metric | Tolerance |
|:--|:--|
| `MEMBERS`, `CC`, `CL`, `SLOC`, `ELOC`, `DIT`, `ERRORS`, `METHODS`, `MAXCC`, `ELOC_PER_MEMBER` | **Exact** |
| `MI` | **Not compared at all** |

**Exact, where layer 1 allowed MI ±2.** Every gated field is an integer count of a syntactic fact
over a checked-in input file, computed by a pinned Roslyn — there is nothing left for a tolerance to
absorb, and the only field that needed one is the field that is no longer compared.

**The gate is on change, not on level.** Nothing here is measured against an absolute "good" value,
and that is not squeamishness: emitted code legitimately has characteristics that would be smells
if hand-written, so a level-based threshold imported from layer 1 would fail honest code. A pull
request that legitimately makes the emitted code larger refreshes the baseline, and the refreshed
diff is what the reviewer approves.

A failure names the model and the metric:

```
Generated code metrics drifted for model 'OrderEventParquetExtensions': test/.../OrderEventParquetExtensions.metrics.txt
  ~ summary: OrderEventParquetExtensions
      CC 310 -> 311
      SLOC 2735 -> 2739
      ELOC 890 -> 892
  ~ changed: M:SampleDomain.Models.OrderEventParquetExtensions.ResolveSchemaField(...)
      CC 8 -> 9
      CL 7 -> 8
      SLOC 52 -> 56
```

## Refreshing

The same one command as layer 1 and the golden files themselves:

```bash
UPDATE_GOLDEN_FILES=true dotnet test test/Parquet.SourceGenerator.Tests/Parquet.SourceGenerator.Tests.csproj \
  --filter "FullyQualifiedName~GoldenCodeGenRegressionTests"   # *.g.cs and *.api.txt
UPDATE_GOLDEN_FILES=true dotnet run scripts/CodeMetrics.cs      # metrics/*.metrics.txt and GoldenFiles/*.metrics.txt
```

Or comment `/update-golden` on a pull request, which runs both in that order and commits the result
— the golden sources first, then the metrics measured over them, so the three artefacts cannot
drift apart.

> The file-based `dotnet run scripts/*.cs` apps need the .NET 10 SDK. If your `PATH` SDK is older,
> run them under `~/.dotnet/dotnet`.

## The baseline as it stands

| Model | Emitter | Members | SLOC | ELOC | CC | Max method CC | CL | Errors | ELOC/member |
|:--|:--|--:|--:|--:|--:|--:|--:|--:|--:|
| `OrderEventParquetExtensions` | v6 | 82 | 2,735 | 890 | 310 | 26 | 82 | 0 | **10.9** |
| `ScalarMetricParquetExtensions` | v6 | 80 | 2,273 | 806 | 253 | 21 | 70 | 0 | **10.1** |
| `NestedOrderParquetExtensions` | v6 | 47 | 2,670 | 1,075 | 287 | 56 | 72 | 6 | **22.9** |
| `ListOrderParquetExtensions` | v6 | 47 | 3,019 | 1,543 | 382 | 97 | 73 | 0 | **32.8** |
| `LegacyRecordParquetLegacyExtensions` | V5 | 7 | 481 | 115 | 42 | 9 | 39 | 0 | **16.4** |

**The ratio separates the feature set cleanly.** Flat scalar models cost about **10 executable
lines per emitted member**. Nested compound members cost **2.3×** that, and row-level lists **3.2×**
— and neither adds a single public member for it. Lists and nesting are, on this measure, the
expensive capabilities in the generator: they buy no new surface and roughly triple the emitted
machinery behind the surface that already exists.

### The most complex emitted methods

| Method | CC | SLOC | ELOC | MI |
|:--|--:|--:|--:|--:|
| `ListOrderParquetExtensions.WriteParquetRowGroupAsync` | **97** | 1,148 | 534 | 0 |
| `NestedOrderParquetExtensions.WriteParquetRowGroupAsync` | 56 | 608 | 257 | 12 |
| `ListOrderParquetExtensions.ReadParquetArrayAsync` | 34 | 203 | 143 | 22 |
| `ListOrderParquetExtensions.ReadParquetAsync` | 34 | 226 | 147 | 22 |
| `ListOrderParquetExtensions.ReadBufferSequentialArrayAsync` | 32 | 190 | 139 | 23 |

### ⚠️ What that first row means

`WriteParquetRowGroupAsync` for a four-property list model carries **cyclomatic complexity 97 in a
single 1,148-line method**. For comparison, the worst hand-written method in the repository —
`TargetParser.CollectMembers`, which [21](./21-CODE-METRICS.md) calls the alarming finding of layer
1 — is **105**. The generator emits, from a four-property model, a method within 8% of the worst
method anybody in this repository has ever hand-written.

The difference is that nobody has to *maintain* the emitted one. But somebody does have to **debug
through it**: a consumer stepping into a failing write lands in 1,148 lines with 97 paths, in a file
they did not write and cannot edit. That is the cost this measurement exists to make visible.

It is also a **size** problem rather than a tangledness problem, in exactly the way
`CodeEmitter.EmitWriteRowGroupAsync` is — the complexity is a per-column ladder, unrolled
deliberately for throughput, not a thicket of unrelated conditions. Any argument for splitting it
has to be made against the performance case in
[11](./11-PERFORMANCE-OPTIMIZATION-FINDINGS.md), not on the number alone.

## What is deliberately *not* here

- **No absolute threshold, and no analyzer rules.** `CA1502`/`CA1505`/`CA1506` are scoped to
  `[src/**.cs]` ([21](./21-CODE-METRICS.md#the-analyzer-rules)) and stay there. Enabling them on
  emitted code would fail every consumer's build for a design decision they did not make, which is
  why the emitted files carry `#pragma warning disable` headers in the first place.
- **No `metrics/` entry.** These baselines live beside the golden files they describe, not in
  `metrics/`, because their lifecycle is the golden files' lifecycle, not `src/`'s.
- **Not a governed API surface.** [18](./18-API-CHANGE-CONTRACT.md) governs `*.api.txt`,
  `src/api/seams.txt` and the `PublicAPI.*.txt` files. A `*.metrics.txt` is not a catalogue and
  adds no public member, and `GoldenFiles/Models/*.cs` is `Compile`-removed from every assembly, so
  neither needs a `docs/api/LEDGER.md` entry. Same conclusion layer 1 reached for `metrics/*`,
  reached independently for these files.

## Related

- [17 - Generated Public API Baselines](./17-GENERATED-API-BASELINES.md) — the `.api.txt`
  companion, and the source of the `MEMBERS` count.
- [21 - Code Metrics Baselines & The Complexity Ratchet](./21-CODE-METRICS.md) — layer 1, the
  hand-written half, and the shared format rationale.
- [11 - Performance Optimization Findings](./11-PERFORMANCE-OPTIMIZATION-FINDINGS.md) — why the
  emitted methods are long on purpose.
