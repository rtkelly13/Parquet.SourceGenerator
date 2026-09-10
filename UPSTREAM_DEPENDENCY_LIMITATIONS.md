# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` requires definition-level memory whenever `DataField.MaxDefinitionLevel > 0`. This prevents the generator from implementing the `NullCount == 0` definition-level bypass for nullable fields.

Track the limitation and future upstream fixes in [issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for a safe non-nullable read path or an API that permits omitting definition-level output before revisiting the optimization.

<<<<<<< HEAD
## Parquet.Net 6.1.0 Column Decode Always Allocates

`ParquetRowGroupReader.ReadAsync` and `ReadRawAsync<T>` both decode a column chunk into an
internally allocated array and then copy into the caller's `Memory<T>`. Measured on a single
`double` column, 20,000 rows, Apple M1 / .NET 9: ~7.8 bytes per row allocated on both entry
points, i.e. one array the size of the decoded data per column per row group — identical with and
without a definition-levels buffer supplied.

The consequence for the struct-of-arrays batch API ([issue #147](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/147))
is that "zero allocation" can only mean zero *domain object* allocation. Genuinely allocation-free
reads need an upstream decode-into-caller-buffer entry point.
=======
## Parquet.Net 6.1.0 Page Checksums Are Not Verified

Parquet's `PageHeader.crc` is optional, and Parquet.Net neither writes nor verifies it. A single
flipped bit inside an encoded data page therefore decodes to a different but perfectly well-formed
value, and a flipped byte in a column chunk's metadata can send the reader at a different — still
valid — page offset. The property-based corruption suite
(`test/Parquet.SourceGenerator.Tests/PropertyBased/CorruptedParquetTests.cs`) found both, so its
random-bit-flip cases assert only that the read terminates without exhausting memory; structural
damage (truncation, bad magic, bad footer length) is still required to be rejected outright.

Nothing in the generated code can close this gap: detection has to happen where the page is decoded.
Revisit if Parquet.Net gains CRC emission and verification.

## Absent Optional Columns Are Rejected By The Generated Reader

Not an upstream limitation, but found while writing the above and recorded here so it is not lost: a
valid Parquet file that simply omits an optional column is rejected with
`ParquetException: '<column>' does not exist in this file`. The generated `ResolveSchemaField`
already handles the case and falls back to the compile-time field, but the read path then calls
`GetStatistics`/`ReadAsync` with a field the file does not contain. Pinned by
`SupportedSchemaPropertyTests.AbsentNullableColumnIsRejectedToday`.
>>>>>>> origin/main
