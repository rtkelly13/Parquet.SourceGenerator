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

8. **[07 - Known Limitations & Remediation Plan](./07-KNOWN-LIMITATIONS.md)**
   - Audited gaps between intended design and observed behaviour, with severity markers.
   - Parquet.Net version/TFM matrix and what a net472-capable backend actually requires.
   - Sequenced remediation order; items are marked ✅ as they are closed.

---

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
