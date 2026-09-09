# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` requires definition-level memory whenever `DataField.MaxDefinitionLevel > 0`. This prevents the generator from implementing the `NullCount == 0` definition-level bypass for nullable fields.

Track the limitation and future upstream fixes in [issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for a safe non-nullable read path or an API that permits omitting definition-level output before revisiting the optimization.

## Parquet.Net 6.1.0 Write-Side Column Shapes

Verified against the decompiled `Parquet.ParquetRowGroupWriter` from the 6.1.0 package while
designing the direct columnar handoff (issue #137). These constrain what a zero-copy columnar write
API can accept.

1. **`WriteAsync<T>` is constrained `where T : struct`.** There is no generic overload accepting a
   reference-typed column, so a `string` column's zero-copy shape is
   `ReadOnlyMemory<ReadOnlyMemory<char>?>` and a binary column's is
   `ReadOnlyMemory<ReadOnlyMemory<byte>?>`, not `ReadOnlyMemory<string?>` /
   `ReadOnlyMemory<byte[]?>`. The generated columnar batch struct exposes the memory-of-memory shape
   for that reason, which is less obvious than the issue's original sketch but is the only form that
   copies nothing.

2. **The convenience overloads rent and copy.** `WriteAsync(DataField, IReadOnlyCollection<string?>)`
   and `WriteAsync(DataField, IReadOnlyCollection<byte[]?>)` each rent a
   `ReadOnlyMemory<...>?[]` from `ArrayPool.Shared`, convert every element into it, and delegate to
   the generic overload. A columnar API routed through them would reintroduce exactly the per-write
   rental it exists to remove, so the generator does not use them.

3. **`WriteAllPartsAsync<T>` is the only way to supply definition levels explicitly**, and it is
   likewise `where T : struct`. A nullable value column therefore has to be presented as packed
   non-null payloads plus a separate `ReadOnlyMemory<int>` of definition levels — there is no
   overload that takes a `ReadOnlyMemory<T?>` and derives the levels without an intermediate buffer.
   This is why the generated batch exposes two members per nullable value column rather than one.

None of these are bugs; they are the shape of the low-level API. They are recorded here because each
one closed off a friendlier API design during #137, and a future Parquet.Net version relaxing any of
them would let the generated surface get simpler.
