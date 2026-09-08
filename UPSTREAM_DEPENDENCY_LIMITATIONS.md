# Upstream Dependency Limitations

## Parquet.Net 6.1.0 Nullable Reads

`ParquetRowGroupReader.ReadRawAsync<T>` requires definition-level memory whenever `DataField.MaxDefinitionLevel > 0`. This prevents the generator from implementing the `NullCount == 0` definition-level bypass for nullable fields.

Track the limitation and future upstream fixes in [issue #150](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/150). Scan Parquet.Net for a safe non-nullable read path or an API that permits omitting definition-level output before revisiting the optimization.

## Apache.Arrow 23.0.0 — Native AOT readiness unproven

The conditionally emitted Arrow ingestion bridge ([issue #177](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/177))
has **not** been exercised through the `AotTest` harness. `Parquet.SourceGenerator.AotTest` deliberately
does not reference Apache.Arrow, so the native publish CI job proves nothing about it either way.

The emitted bridge itself is reflection-free — it is ordinary generated text calling Apache.Arrow's
public API — but Apache.Arrow's own AOT/trimming posture is unverified here. Until someone runs a
`PublishAot` harness that references Apache.Arrow and reports the ILCompiler warnings, treat the
bridge as **AOT-unsupported**.

## Apache.Arrow 23.0.0 — no zero-copy `ReadOnlyMemory<T>` view over a value buffer

`ArrowBuffer` exposes `Memory` (`ReadOnlyMemory<byte>`) and `Span`, and `PrimitiveArray<T>` exposes
`Values` (`ReadOnlySpan<T>`), but there is no typed `ReadOnlyMemory<T>` accessor — and the BCL has no
`MemoryMarshal.Cast` for `Memory<T>`. Parquet.Net's `WriteAsync<T>` takes `ReadOnlyMemory<T>`, so the
generated bridge carries its own `MemoryManager<T>` that reinterprets the byte buffer
(`MemoryMarshal.CreateSpan` + `MemoryMarshal.Cast`), guarded to `NET6_0_OR_GREATER` with a copying
fallback below that. A typed `ArrowBuffer.Memory<T>()` upstream would delete that helper.

## Apache.Arrow 23.0.0 — `LargeBinary` / `LargeUtf8` have no 64-bit offset array types in scope

The bridge rejects `LargeUtf8` (as #177 requires) and also `LargeBinary`, because consuming 64-bit
offsets would need a separate materialization path. Revisit if Apache.Arrow grows a uniform
offset accessor across `BinaryArray` and `LargeBinaryArray`.
