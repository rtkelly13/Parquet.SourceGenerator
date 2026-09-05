# TODO: PARQUET.SOURCEGENERATOR

- [x] Issue #142: Expose Parquet format-level dictionary encoding and column encoding hints
  - [x] Add `ParquetColumnEncoding` enum to `Parquet.SourceGenerator.Attributes`
  - [x] Add `Encoding` property to `ParquetColumnAttribute`
  - [x] Add `DictionaryEncodingThreshold`, `DictionaryEncodingSampleSize`, and `ColumnEncodingHints` to `ParquetSerializerOptions`
  - [x] Update `PublicAPI.Unshipped.txt` in Attributes and Generator
  - [x] Update `PropertyModel` with `ColumnEncoding Encoding = ColumnEncoding.Default`
  - [x] Update `TargetParser` to extract `Encoding` from `[ParquetColumn]`
  - [x] Update `CodeEmitter.EmitBuildFormatOptions` to generate threshold assignments and `ColumnEncodingHints` mappings
  - [x] Add unit tests for attributes, options, Roslyn snapshots, and metadata assertions
  - [x] Verify coverage gates (>=85% line, >=70% branch) and formatting (`csharpier`, `whitespace`)
