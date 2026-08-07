# Performance Benchmarks & Baseline Reports

`Parquet.SourceGenerator` is designed for high-performance C# Parquet serialization and deserialization, achieving zero reflection, Native AOT compatibility, and minimum memory allocation.

---

## ⚡ Headline Performance Baseline

The following baseline metrics compare **`Parquet.SourceGenerator`** against **`ParquetSerializer` v6** (reflection baseline) at **100,000 items** scale:

| Operation | Scale | Reflection Baseline | Source Generator | Speedup | Memory Reduction |
|:--- |:---:|:---:|:---:|:---:|:---:|
| **File Serialization (Write)** | 100,000 items | 7.71 ms (12.63 MB) | **3.65 ms** (**7.02 MB**) | ⚡ **2.1x faster** | 📉 **44% less memory** |
| **Streaming Batched Write** | 100,000 items | 7.71 ms (12.63 MB) | **4.41 ms** (**5.45 MB**) | ⚡ **1.8x faster** | 📉 **57% less memory** |
| **File Deserialization (Read)** | 100,000 items | 5.05 ms (4.62 MB) | **6.29 ms** (**10.91 MB**) | 1.25x baseline | 2.36x alloc |
| **Guid Serialization** | 100,000 items | 14.70 ms (29.50 MB) | **9.11 ms** (**18.10 MB**) | ⚡ **1.6x faster** | 📉 **39% less memory** |

---

## 🛠️ Running Benchmarks Locally

You can execute the full BenchmarkDotNet suite locally using the .NET CLI:

```bash
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj
```

To run a specific benchmark class or method filter:
```bash
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj -- --filter "*Scaling*"
```

---

## 🤖 Automated CI Benchmark Updates

The GitHub Actions performance workflow (`.github/workflows/benchmarks.yml`) automatically executes on manual dispatch or scheduled runs:
1. Runs BenchmarkDotNet across `ScalingSerializationBenchmark`, `ScalingDeserializationBenchmark`, and `GuidInterchangeBenchmark`.
2. Executes the native .NET tool `tools/BenchmarkSummaryGenerator` to format a clean 4-row executive summary table.
3. Automatically opens a Pull Request updating `README.md` and `PACKAGE_README.md` whenever performance baseline numbers change.

---

## 🔗 Related Documentation

* [Testing Strategy & Benchmarks (05)](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/docs/05-TESTING-STRATEGY-AND-BENCHMARKS.md)
* [Vision & Architecture (01)](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/docs/01-VISION-AND-ARCHITECTURE.md)
