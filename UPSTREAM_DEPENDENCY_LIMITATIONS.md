# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` takes definition levels as `Memory<int>?`, but rejects
`null` whenever `DataField.MaxDefinitionLevel > 0`:

```
ArgumentException: Definition levels buffer is required for field 'v' (MaxDefinitionLevel = 1)
```

It throws even when the caller has already established from the column-chunk statistics that the
chunk holds no nulls. The generator therefore cannot skip definition-level rental and decoding for
nullable fields; the zero-null fast path (issue #150) rents a scratch level buffer, hands it over
and discards the decoded levels.

What the generator *can* do without upstream help — and now does — is skip the `T?[]` staging
buffer: with `NullCount == 0` the dense page values line up one-for-one with the rows, so the
reader decodes straight into a non-nullable `T[]` lane and lifts each value into `T?` at
materialization time.

Removing the remaining definition-level cost needs an upstream Parquet.Net API change, such as a
safe non-nullable read path for nullable schema fields, or an overload that permits omitting
definition-level output when the caller has already established `NullCount == 0`. Track the
limitation and future upstream fixes in
[issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for
such an API before revisiting.
