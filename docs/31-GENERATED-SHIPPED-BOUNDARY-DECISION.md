# Generated/shipped boundary decision (#237)

## 0.1 decision

The 0.1 release keeps format-shaped orchestration in generated code. The generated extension owns
the `ParquetReader`/`ParquetWriter` calls, backend-specific field handling, and the typed
materialization loop. The shipped Attributes package owns only consumer-facing annotations,
options, and value types.

This is a deliberate scope decision, not a claim that a shared boundary could never work. The
modern and classic emitters target materially different Parquet.Net APIs, while the current public
surface is still settling around the read builder, column catalog, and row-group metadata seams.
Moving one path before those seams are stable would create a second abstraction boundary that would
need to be redesigned during the 0.1 freeze.

## Follow-up gate

Reopen the portability experiment after the builder and backend-neutral catalog/accessor contracts
are stable. The experiment must then port one sequential buffer read, compare TPC-H LineItem and
Adult Census throughput, record the Native AOT binary-size delta, and use a materialization-focused
allocation/time denominator. End-to-end throughput alone is insufficient because Parquet.Net's
encoding and compression dominate that measurement.

Until that evidence exists, generated code remains the lower-risk boundary for 0.1 and #220–#222
remain post-freeze design work rather than dependencies of the release.
