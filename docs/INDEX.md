# Parquet.SourceGenerator Documentation Hub

Welcome to the **Parquet.SourceGenerator** documentation repository. This folder contains the architectural specifications, API designs, implementation details, and project roadmap for building a production-grade, high-throughput C# Source Generator for the [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) library.

---

## 📚 Documentation Index

1. **[01 - Vision & Architecture](file:///Users/ryankelly/code/personal/Parquet.SourceGenertor/docs/01-VISION-AND-ARCHITECTURE.md)**
   - Problem statement & rationale (Reflection overhead, Native AOT limitations in standard `Parquet.Net`).
   - Architectural goals & core design principles.
   - High-level design diagram and memory/performance profile expectations.

2. **[02 - API Design & Attributes](file:///Users/ryankelly/code/personal/Parquet.SourceGenertor/docs/02-API-DESIGN-AND-ATTRIBUTES.md)**
   - Public code-generator attributes (`[ParquetSerializable]`, `[ParquetColumn]`, `[ParquetIgnore]`, etc.).
   - Generated code API contracts (`TypeParquetSerializer`, `Schema`, `WriteAsync`, `ReadAsync`).
   - Developer ergonomics and code examples.

3. **[03 - Incremental Generator Pipeline](file:///Users/ryankelly/code/personal/Parquet.SourceGenertor/docs/03-INCREMENTAL-GENERATOR-PIPELINE.md)**
   - Roslyn `IIncrementalGenerator` implementation architecture.
   - Syntax provider filtering, semantic symbol extraction, and equatable models.
   - Code generation builders and Roslyn Diagnostic Descriptors (`PARQ001` to `PARQ099`).

4. **[04 - Roadmap & Open Source Blueprint](file:///Users/ryankelly/code/personal/Parquet.SourceGenertor/docs/04-ROADMAP-AND-CONTRIBUTING.md)**
   - Multi-phase implementation roadmap (Phase 1 MVP to Phase 5 Native AOT & Benchmarks).
   - Testing strategy (Unit tests with `GeneratorDriver`, integration tests, snapshooting with `Verify`).
   - CI/CD workflow, NuGet packaging, and contribution rules.

5. **[05 - Testing Machinery & Benchmarking Strategy](file:///Users/ryankelly/code/personal/Parquet.SourceGenertor/docs/05-TESTING-STRATEGY-AND-BENCHMARKS.md)**
   - Roslyn Generator Unit & Snapshot testing (`Verify.SourceGenerators`).
   - Incremental caching verification (`TrackIncrementalSteps`).
   - Binary data roundtrip testing with `Parquet.Net`.
   - Native AOT & trim validation strategy.
   - BenchmarkDotNet performance benchmarking setup.

6. **[06 - Test Data Specification & Symmetrical Benchmarking](file:///Users/ryankelly/code/personal/Parquet.SourceGenertor/docs/06-TEST-DATA-SPECIFICATION.md)**
   - Deterministic test dataset matrix (01 through 05).
   - Mathematical row generation formulas and null rules.
   - Python (`PyArrow` via `uv`) and C# (`Parquet.Net` via `dotnet run`) dataset generation tooling.

---

## ⚡ Quick Summary of Intent

`Parquet.SourceGenerator` is designed to eliminate the reliance on runtime reflection when serializing and deserializing C# domain models (classes, records, structs) to and from Apache Parquet files using `Parquet.Net`. 

By emitting specialized, strongly-typed column readers and writers at compile time, `Parquet.SourceGenerator` delivers:
- **Zero Reflection & Maximum Throughput**: Direct array transfers between C# memory and Parquet columns.
- **100% Native AOT & Trimming Compatible**: Safe for serverless lambdas, microservices, and desktop CLI tools targeting .NET 8 / .NET 9+.
- **Compile-Time Safety**: Catches schema mismatches and unsupported data types before running code.
