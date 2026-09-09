# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` requires definition-level memory whenever `DataField.MaxDefinitionLevel > 0`. This prevents the generator from implementing the `NullCount == 0` definition-level bypass for nullable fields.

Track the limitation and future upstream fixes in [issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for a safe non-nullable read path or an API that permits omitting definition-level output before revisiting the optimization.

## Parquet.Net 6.1.0 retains the most recently written string column

Writing a row group whose columns include `ReadOnlyMemory<char>` leaves the values of the
*last-written* string column reachable after `ParquetWriter` has been disposed. Verified with weak
references while building the column-pipelined write path (issue #136):

- Values of every earlier string column are collected; only the final one survives.
- The retention is identical under the row-oriented and column-pipelined strategies, and the
  generated code returns every string buffer with `clearArray: true` in both, so it is not our
  pooled buffers holding them.
- Draining and clearing the `ArrayPool<ReadOnlyMemory<char>?>`, `ArrayPool<char>` and
  `ArrayPool<byte>` shared buckets after the write does not release them either, which rules out a
  dirty pooled array anywhere in the pipeline.

The residency is bounded — one column's worth of values, released as soon as the next string column
is written — so it is not a growing leak, but a caller writing one final large string column and
then holding the writer's stream will keep that column's strings alive. `ColumnPipelinedWriteTests`
works around it by placing a non-string column last and asserting only on the columns behind the
reused slot.
