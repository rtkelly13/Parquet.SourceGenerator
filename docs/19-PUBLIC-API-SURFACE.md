# 19 - Public API Surface

> **Stub.** This file currently records one rule — the options-vs-parameters rule from
> [#218](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/218).
> [#216](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/216) expands it into the full
> surface audit: the source × shape × execution grid, the four catalogued defects, the naming
> grammar, and the fate of the existing flat method names. The rule below is written to survive
> that expansion unchanged.
>
> The issue text for #218 and #216 names this file `docs/16-PUBLIC-API-SURFACE.md`. `16`, `17` and
> `18` were taken by the time the work started, so it is numbered `19`.

## The options-vs-parameters rule

> **`ParquetSerializerOptions` is the single home for configuration.**
>
> A positional parameter exists only where call-site ergonomics genuinely demand it. Where one
> exists, its XML doc states explicitly that it takes precedence over the options property, and the
> options property's XML doc says the same from the other side.

### Why an option needs one home

A knob with two homes has a precedence rule, and a precedence rule is invisible from the signature.
`ReadParquetParallelAsync(bytes, maxDegreeOfParallelism, options, ct)` gave a caller who set both no
way to know which won without reading the emitter — and the answer differed between the two options
that had this shape, which is the tell that nobody had decided it:

| Option | Old resolution | Effect |
|:---|:---|:---|
| `rowGroupSize` | `rowGroupSize ?? options.RowGroupSize` | Parameter wins when supplied; `int?` genuinely expresses "unset". |
| `maxDegreeOfParallelism` | `mdop > 0 ? mdop : (options.MaxDegreeOfParallelism > 0 ? … : ProcessorCount)` | Parameter wins when positive. A caller passing `0` or a negative value did **not** get an error and did **not** get their value — the argument was silently discarded and the options property used instead. |

The cost is not only the caller's confusion. Every new option arrives at the same fork with no rule
to resolve it, and answers it by coin flip. The surface then grows two ways of saying everything.

### What the rule selected

Both duplicates were **deleted**, keeping the options property in each case:

- `maxDegreeOfParallelism` removed from `ReadParquetParallelAsync` and
  `ReadParquetParallelArrayAsync` (both the `Stream` and the `ReadOnlyMemory<byte>` overload).
- `rowGroupSize` removed from `WriteParquetBatchedAsync` and from the `IAsyncEnumerable<T>`
  overload of `WriteParquetAsync`, on both the modern and the classic emitter.

The package is `0.0.x`, and the release-cadence note in
[04 - Roadmap](./04-ROADMAP-AND-CONTRIBUTING.md) permits a break there. Deleting a duplicate is
preferable to documenting a precedence rule nobody reads: the documented rule survives only as long
as everyone keeps reading it, while the deleted parameter cannot be got wrong.

Neither parameter cleared the "call-site ergonomics genuinely demand it" bar. Both sit behind at
least one other optional parameter, so both were already reached by name (`rowGroupSize: 10_000`)
rather than by position, and `new ParquetSerializerOptions { RowGroupSize = 10_000 }` is the same
shape of expression at the call site. And on the `Stream` overloads `maxDegreeOfParallelism` was
inert anyway — that reader is sequential by construction, because an arbitrary `Stream` cannot be
handed to more than one `ParquetReader`.

### Applying it to the next option

1. The option is a property on `ParquetSerializerOptions`. That is the default and needs no
   argument.
2. A positional parameter is added **only** with a stated ergonomic reason — that the option is
   required on essentially every call of that method, or that it is the method's subject rather
   than its configuration. "It reads slightly shorter" is not one.
3. If a parameter is added, both XML docs state the precedence: the parameter's doc says it wins,
   the property's doc says the parameter wins. Both, so that whichever the caller reads first tells
   them the truth.
4. Sentinel values are not a precedence mechanism. `int?` expresses "unset"; `50_000` and `-1` do
   not, and both have already caused this repository a bug
   ([07 §3.2](./07-KNOWN-LIMITATIONS.md)).

Every change to the emitted surface goes through the catalogue and ledger in
[18 - The API Change Contract](./18-API-CHANGE-CONTRACT.md), which is where the decision is
recorded.
