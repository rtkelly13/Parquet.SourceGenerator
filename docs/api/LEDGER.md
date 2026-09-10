# API Change Ledger

Every line added to a governed catalogue — an emitted-API `*.api.txt`, `src/api/seams.txt`, or a
`PublicAPI.Unshipped.txt` — needs an entry here classifying its semver impact. Newest first.

The rule, the three surfaces and the author process are in
[18 - API Change Contract](../18-API-CHANGE-CONTRACT.md). The buckets are `additive-minor`,
`breaking-major`, `internal` and `generated-shape`.

> **Everything dated before 2026-09-10 was catalogued retrospectively.** The single
> `0.0.x inherited surface` entry below covers the whole surface that existed when this contract
> was introduced. Those members were **not** reviewed under this contract, no per-member rationale
> was written for them, and none should be read as approved. That is the point of seeding it this
> way: the freeze diff for [#230](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/230)
> is then exactly the set of entries dated on or after 2026-09-10, which *were* reviewed.

<!-- Add new entries directly below this line, newest first. -->

### 2026-09-10 — `0.0.x inherited surface`

- **Surface:** emitted, seam
- **Semver:** generated-shape
- **Issue:** [#215](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/215),
  [#230](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/230)
- **Rationale:** Retrospective seeding. Covers all **159** emitted public members catalogued across
  the five golden models by #215 — `OrderEventParquetExtensions` (55),
  `ScalarMetricParquetExtensions` (53), `NestedOrderParquetExtensions` (22),
  `ListOrderParquetExtensions` (22), `LegacyRecordParquetLegacyExtensions` (7) — and the **3**
  internal seams seeded into `src/api/seams.txt`:
  `CodeEmitter.EmitResolveFieldLine`, `CodeEmitter.EmitReadWithNullBypass` and
  `CodeEmitter.GetBranchlessNonNullExpression`. Writing 162 retroactive rationales would have
  produced 162 fabrications; recording one honest statement that the surface grew by accretion is
  worth more than 162 invented ones.
- **Alternatives considered:** One entry per existing member — rejected: the rationales would have
  been reconstructed after the fact and would read as approval that never happened, which is
  exactly the failure this ledger exists to prevent. Backdating the contract to the commits that
  introduced each member — rejected: the reviews those members actually received did not ask the
  questions this contract asks, so claiming they did would be false.
- **Note:** Pre-1.0 stance. `0.0.x` permits breaking changes without a major bump, so the bucket on
  an entry does not gate the release — it records that the call was made and by whom. At `0.1.0`
  (#230) this file becomes the freeze record.
