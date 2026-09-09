# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` requires definition-level memory whenever `DataField.MaxDefinitionLevel > 0`. This prevents the generator from implementing the `NullCount == 0` definition-level bypass for nullable fields.

Track the limitation and future upstream fixes in [issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for a safe non-nullable read path or an API that permits omitting definition-level output before revisiting the optimization.

## Parquet.Net 6.1.0 Column Decode Always Allocates

`ParquetRowGroupReader.ReadAsync` and `ReadRawAsync<T>` both decode a column chunk into an
internally allocated array and then copy into the caller's `Memory<T>`. Measured on a single
`double` column, 20,000 rows, Apple M1 / .NET 9: ~7.8 bytes per row allocated on both entry
points, i.e. one array the size of the decoded data per column per row group — identical with and
without a definition-levels buffer supplied.

The consequence for the struct-of-arrays batch API ([issue #147](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/147))
is that "zero allocation" can only mean zero *domain object* allocation. Genuinely allocation-free
reads need an upstream decode-into-caller-buffer entry point.
