<p align="center">
  <img src="./assets/logo.svg" alt="Parquet.SourceGenerator" width="104" height="104">
</p>

# Parquet.SourceGenerator Documentation Hub

Welcome to the **Parquet.SourceGenerator** documentation repository. This folder contains the architectural specifications, API designs, implementation details, and project roadmap for building a production-grade, high-throughput C# Source Generator for the [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) library.

---

## 📚 Documentation Index

1. **[01 - Vision & Architecture](./01-VISION-AND-ARCHITECTURE.md)**
   - Problem statement & rationale (Reflection overhead, Native AOT limitations in standard `Parquet.Net`).
   - Architectural goals & core design principles.
   - High-level design diagram and memory/performance profile expectations.

2. **[02 - API Design & Attributes](./02-API-DESIGN-AND-ATTRIBUTES.md)**
   - Public code-generator attributes (`[ParquetSerializable]`, `[ParquetColumn]`, `[ParquetIgnore]`, etc.).
   - Generated code API contracts (`TypeParquetSerializer`, `Schema`, `WriteAsync`, `ReadAsync`).
   - Developer ergonomics and code examples.

3. **[03 - Incremental Generator Pipeline](./03-INCREMENTAL-GENERATOR-PIPELINE.md)**
   - Roslyn `IIncrementalGenerator` implementation architecture.
   - Syntax provider filtering, semantic symbol extraction, and equatable models.
   - Code generation builders and Roslyn Diagnostic Descriptors (`PARQ001` to `PARQ099`).

4. **[04 - Roadmap & Open Source Blueprint](./04-ROADMAP-AND-CONTRIBUTING.md)**
   - Multi-phase implementation roadmap (Phase 1 MVP to Phase 5 Native AOT & Benchmarks).
   - Testing strategy (Unit tests with `GeneratorDriver`, integration tests, snapshooting with `Verify`).
   - CI/CD workflow, NuGet packaging, and contribution rules.

5. **[05 - Testing Machinery & Benchmarking Strategy](./05-TESTING-STRATEGY-AND-BENCHMARKS.md)**
   - Roslyn Generator Unit & Snapshot testing (`Verify.SourceGenerators`).
   - Incremental caching verification (`TrackIncrementalSteps`).
   - Binary data roundtrip testing with `Parquet.Net`.
   - Native AOT & trim validation strategy.
   - BenchmarkDotNet performance benchmarking setup.

6. **[Performance Benchmarks & Baseline Reports](./BENCHMARKS.md)**
   - Automated BenchmarkDotNet performance metrics.
   - Speedup ratios and memory allocation savings vs `ParquetSerializer` v6.
   - Automated CI benchmark update workflow.

7. **[06 - Test Data Specification & Symmetrical Benchmarking](./06-TEST-DATA-SPECIFICATION.md)**
   - Deterministic test dataset matrix (01 through 05).
   - Mathematical row generation formulas and null rules.
   - Python (`PyArrow` via `uv`) and C# (`Parquet.Net` via `dotnet run`) dataset generation tooling.
   - Cryptographic hash-based regression suite and bit-for-bit determinism validation.


8. **[07 - Known Limitations & Remediation Plan](./07-KNOWN-LIMITATIONS.md)**
   - Audited gaps between intended design and observed behaviour, with severity markers.
   - Parquet.Net version/TFM matrix and what a net472-capable backend actually requires.
   - Sequenced remediation order; items are marked ✅ as they are closed.

9. **[08 - IL Interrogation & Performance Verification](./08-IL-INTERROGATION.md)**
   - Intermediate Language (IL) disassembly and decompilation workflow with `ilspycmd` and `dotnet-inspect`.
   - Automated boxing detection (`box`), devirtualization checks, and branch diffing.

10. **[09 - Performance & Memory Triage with dotnet-dump](./09-PERFORMANCE-TRIAGE-DOTNET-DUMP.md)**
   - Managed memory dump capture and automated SOS triage analysis.
   - Diagnosing heap allocations, buffer leaks, Large Object Heap (LOH), and Pinned Object Heap (POH).

11. **[10 - Native AOT & Type System Guide](./10-NATIVE-AOT-GUIDE.md)**
    - Comprehensive supported types matrix and Native AOT runtime behavior.
    - Deep-dive into `Nullable<T>` value type mechanics, CoreCLR `TypeUnifier.WithVerifiedTypeHandle`, and code-sharing limits.
    - Analysis of the Parquet.Net 6.1.0 `ReadOnlyMemory<T>` shift and resolution via Runtime Directives (`rd.xml`).

12. **[11 - Performance Optimization Findings & Baseline Analysis](./11-PERFORMANCE-OPTIMIZATION-FINDINGS.md)**
    - Empirical performance and IL boxing baseline across generated serializers.
    - Evaluation of zero-boxing string serialization mechanisms and L1 string deduplicator cache efficiency.

13. **[12 - Buffer Reuse & Column Extraction Strategies](./12-BUFFER-REUSE-AND-EXTRACTION-STRATEGIES.md)**
    - Empirical evaluation of Row-Oriented Single Pass vs Column-Pipelined extraction.
    - CPU cache spatial locality vs multi-pass traversal analysis.
    - Eager progressive buffer return mechanics and single `try / finally` exception safety.

14. **[13 - Compiler Diagnostics Reference](./13-COMPILER-DIAGNOSTICS.md)**
     - Complete catalog of compiler diagnostic codes (`PARQ001`–`PARQ011`).
     - Severity, rationale, and remediation examples for every rule.

15. **[14 - Parquet Compatibility Matrix](./14-COMPATIBILITY-MATRIX.md)**
     - Supported generated model types and schema shapes.
     - Modern and classic Parquet.Net product boundaries.
     - Backward, forward, schema-evolution, and semantic compatibility definitions.

16. **[15 - Nested Types: M0 Spike Findings](./15-NESTED-TYPES-SPIKE-FINDINGS.md)**
     - Empirically verified def/rep level conventions for structs, lists, and maps.
     - Parquet.Net 6.1.0 and 4.25.0 nested API traps that shape the #176 emitter design.

17. **[16 - Version And Schema-Evolution Matrix](./16-VERSION-AND-SCHEMA-EVOLUTION.md)**
     - Schema-evolution contract: reordering, extra columns, absent optional and required columns.
     - Producer/consumer/version matrix across Parquet.Net, PyArrow and DuckDB, and the recorded report.
     - Cross-version interoperability between the modern and classic packages.

18. **[17 - Generated Public API Baselines](./17-GENERATED-API-BASELINES.md)**
     - The `.api.txt` signature-only baseline emitted next to every golden file, and its grammar.
     - Why it borrows the `PublicAPI.Shipped.txt` grammar, and the two deliberate deviations.
     - Deterministic ordinal ordering, the `UPDATE_GOLDEN_FILES` refresh path, and the CI gate.

19. **[18 - The API Change Contract](./18-API-CHANGE-CONTRACT.md)**
     - The rule: nothing enters a governed surface without a catalogue line *and* a ledger entry.
     - The three surfaces (emitted / shipped package / internal seams) and their build gates
       `PARQAPI001`, `RS0016` and `PARQAPI002`.
     - Semver buckets, the pre-1.0 stance, the `**Unapproved-by-design:**` escape hatch, and the
       four-step author process.

20. **[19 - Public API Surface & Naming Grammar](./19-PUBLIC-API-SURFACE.md)**
     - The four-axis read grid — source × shape × execution × pushdown — and which cells exist.
     - Eight catalogued surface defects, each checkable against the `*.api.txt` baselines.
     - The options-vs-parameters rule (D1), the naming grammar, and the fate of the flat methods.


---

19. **[20 - Unified Pushdown & the Generated/Shipped Boundary](./20-UNIFIED-PUSHDOWN-API.md)**
     - One inspectable filter replacing four pushdown mechanisms; capability declared by attribute, not at the call site.
     - What stays generated, what ships, and the measurements that decide it.

## ⚡ Quick Summary of Intent

`Parquet.SourceGenerator` is designed to eliminate the reliance on runtime reflection when serializing and deserializing C# domain models (classes, records, structs) to and from Apache Parquet files using `Parquet.Net`. 

By emitting specialized, strongly-typed column readers and writers at compile time, the intended
outcome is:
- **Zero Reflection & Maximum Throughput**: Direct array transfers between C# memory and Parquet columns.
- **Native AOT & Trimming Compatibility**: reflection-free generated code is a precondition for
  AOT and trimming. CI publishes the AOT test project with `-r linux-x64` and runs the resulting
  native binary on every run — `linux-x64` only, and Parquet.Net itself still emits trim and
  AOT-analysis warnings.
- **Compile-Time Safety**: Catches schema mismatches and unsupported data types before running code.

> These documents describe intended design. For what is actually implemented today, see the
> [Known limitations](../README.md#known-limitations) table in the README and the full audit in
> [07 - Known Limitations](./07-KNOWN-LIMITATIONS.md).
