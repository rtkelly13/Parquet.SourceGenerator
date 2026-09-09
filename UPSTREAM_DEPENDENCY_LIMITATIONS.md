# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` requires definition-level memory whenever `DataField.MaxDefinitionLevel > 0`. This prevents the generator from implementing the `NullCount == 0` definition-level bypass for nullable fields.

Track the limitation and future upstream fixes in [issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for a safe non-nullable read path or an API that permits omitting definition-level output before revisiting the optimization.

## Parquet.Net 6.1.0 Has No UTF-8 Byte Surface For String Columns

Issue [#143](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/143) proposed keying the
L1 string cache on raw UTF-8 spans (`ReadOnlySpan<byte>`) so a cache hit skips both the string
allocation *and* the UTF-8 decode. Parquet.Net 6.1.0 exposes no such surface. Probed directly
against the packaged assembly:

- `ParquetRowGroupReader.ReadRawAsync<T>` is constrained to `where T : struct`, so `byte[]` cannot
  be requested through it at all.
- For a UTF8-annotated `BYTE_ARRAY` column the reader's resolved `DataField.ClrType` is
  `System.ReadOnlyMemory<char>` — the library has already decoded UTF-8 to UTF-16 before any
  caller-visible buffer exists. `ReadRawAsync<ReadOnlyMemory<char>>` is therefore the lowest-level
  access available.
- `ParquetOptions.PreferUntypedByteArray` (a static property) does **not** change this. With it set,
  the string field's `ClrType` stays `ReadOnlyMemory<char>` and
  `ReadAsync(field, Memory<byte[]>, …)` throws
  `Field "…" (System.ReadOnlyMemory\`1[System.Char]) is not compatible with type
  System.ReadOnlyMemory\`1[System.Byte]`. The flag only applies to `BYTE_ARRAY` columns that carry
  no UTF8 logical annotation.
- Even the raw path is not allocation free upstream: over 32,561 rows holding six distinct values,
  `ReadRawAsync<ReadOnlyMemory<char>>` produced 32,561 **distinct** backing `char[]` arrays. The
  library allocates one array per row regardless of the caller's API choice, matching the finding
  in PR #208.
- `ReadRawAsync` still demands a definition-levels buffer whenever `MaxDefinitionLevel > 0`
  (see the nullable-reads section above), and returns the value lane **packed** — nulls are absent
  from the value array and must be spread back over the row lane using the definition levels.

What *is* reachable, and what this repository now does, is to read string columns through
`ReadRawAsync<ReadOnlyMemory<char>>` and intern each value with a span-keyed table. That removes
the per-row `string` instantiation (roughly half of the managed read allocation on categorical
data) but cannot remove the per-row `char[]` that Parquet.Net allocates underneath.

The upstream API that would close the gap is a `ReadRawAsync<ReadOnlyMemory<byte>>` (or a
`ReadUtf8Async(DataField, Memory<ReadOnlyMemory<byte>>, …)`) that hands out slices of a single
pooled page buffer. `Utf8StringDeduplicator` in
`test/Parquet.SourceGenerator.Tests/Utf8StringDeduplicatorPrototypeTests.cs` is a tested prototype
of the byte-keyed table that would sit behind it; it is deliberately not wired into the generated
reader, because feeding it would require round-tripping through `string` and defeat the purpose.
