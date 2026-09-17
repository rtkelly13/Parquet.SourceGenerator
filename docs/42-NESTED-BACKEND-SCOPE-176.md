# Nested backend scope (#176)

The 0.1 functional scope is declared by backend rather than by an unsupported promise of full parity:

- Modern Parquet.Net v6 supports the generated `StructField` and list-of-leaf/POCO paths already
  covered by the golden and AOT work.
- Classic Parquet.Net v4/v5 remains flat-only for generated models in 0.1.
- Maps and nested compound shapes not covered by the modern golden corpus remain unsupported on both
  backends and must produce the existing diagnostic rather than silently flattening.

This is the compatibility boundary consumers can rely on for 0.1. The full both-backend implementation
requested by the original issue remains a post-0.1 feature: it needs legacy definition/repetition
level round trips, map semantics, AOT coverage, property-based fuzzing, and external interoperability
before it can be advertised.

This closes the 0.1 scope question for #176; the parity implementation remains a post-freeze
follow-up.
