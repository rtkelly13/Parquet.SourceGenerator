# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` requires definition-level memory whenever `DataField.MaxDefinitionLevel > 0`. This prevents the generator from implementing the `NullCount == 0` definition-level bypass for nullable fields.

Track the limitation and future upstream fixes in [issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for a safe non-nullable read path or an API that permits omitting definition-level output before revisiting the optimization.

## Apache.Arrow 23.0.0 Export Bridge

Verified under Native AOT: `test/Parquet.SourceGenerator.AotTest` references `Apache.Arrow`, and its
`Arrow RecordBatch export (#178)` check runs in the published native binary. The publish produces no
new IL warnings — the only ones remaining are the pre-existing `IL2104`/`IL3053` against
`Parquet.dll`. No escape hatch is needed today.

Limitations that shape the mapping table in [docs/14](docs/14-COMPATIBILITY-MATRIX.md):

- **No UUID logical type.** The C# library exposes `FixedSizeBinaryType` but no UUID extension type,
  so `Guid` columns export as `fixed_size_binary(16)` in RFC 4122 byte order rather than as a
  self-describing UUID.
- **`Decimal128Array` has no buffer-level constructor.** Every other fixed-width array can be built
  from an `ArrowBuffer` over a copied span; decimals must go through `Decimal128Array.Builder`,
  which converts value by value.
- **No zero-copy path from Parquet.Net rentals.** Arrow buffers must own their memory and the
  generator returns its `ArrayPool` rentals at the end of each row group, so a copy per column per
  batch is structural, not an oversight.
