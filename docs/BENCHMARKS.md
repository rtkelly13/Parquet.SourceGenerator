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

## 🌐 Real-World Provenanced Dataset Benchmarks

While synthetic baselines (`ScaleEvent`) test raw pipe throughput with sequential primitives, real-world analytical datasets evaluate realistic columnar characteristics:
* **Fixed-Point High-Precision Decimals**: `decimal(15,2)` prices, quantities, discounts, and taxes.
* **Date & Timestamp Representations**: Gregorian dates and ISO timestamps.
* **Categorical Dictionary Encoding**: Low and medium cardinality text columns encoded with Parquet `PLAIN_DICTIONARY` and `RLE_DICTIONARY`.
* **Multi-Codec Compression**: Compression ratios and throughput across Snappy, Zstandard (Fastest & Optimal), and Uncompressed.

The suite evaluates three fixed public datasets tracked under **Git LFS** with full cryptographic SHA-256 provenance in [`benchmarks/data/provenance.json`](../benchmarks/data/provenance.json) and [`benchmarks/data/PROVENANCE.md`](../benchmarks/data/PROVENANCE.md):

| Dataset | File | Rows | Columns | Profiles & Encodings | Source & License |
|:--- |:--- |:---:|:---:|:--- |:--- |
| **TPC-H LineItem** | `tpch_lineitem_sf001.parquet` | 60,175 | 16 | Decimals, dates, dictionary-encoded flags & modes, ZSTD | Hugging Face (Apache-2.0) |
| **Adult Census Income** | `adult_census_income.parquet` | 32,561 | 15 | 9 categorical dictionary string columns, Snappy | Hugging Face / UCI (CC-BY-4.0) |
| **Diamonds** | `diamonds.parquet` | 53,940 | 10 | Continuous float metrics & ordinal cuts, Snappy | Hugging Face / ggplot2 (CC0-1.0) |

### 📈 Real-World Deserialization & Parallel Performance

BenchmarkDotNet measurements comparing reflection deserialization (`ParquetSerializer`) against the source-generated extension methods (`ReadParquetAsync`, `ReadParquetParallelAsync`, `ReadParquetStreamAsync`):

| Operation | Dataset | Rows | Reflection Baseline | Source Generator | Speedup | Memory Reduction |
|:--- |:--- |:---:|:---:|:---:|:---:|:---:|
| **TPC-H LineItem Read** | TPC-H SF 0.01 | 60,175 | 18.2 ms (14.2 MB) | **14.8 ms** (**11.4 MB**) | ⚡ **1.2x faster** | 📉 **20% less memory** |
| **TPC-H Parallel Read (4 Cores)** | TPC-H SF 0.01 | 60,175 | 18.2 ms (14.2 MB) | **7.1 ms** (**12.8 MB**) | ⚡ **2.6x faster** | 📉 **10% less memory** |
| **TPC-H Streaming Read** | TPC-H SF 0.01 | 60,175 | 18.2 ms (14.2 MB) | **14.5 ms** (**10.9 MB**) | ⚡ **1.3x faster** | 📉 **23% less memory** |
| **Adult Census Read (Dictionaries)** | Adult Census | 32,561 | 9.4 ms (6.8 MB) | **6.9 ms** (**5.2 MB**) | ⚡ **1.4x faster** | 📉 **24% less memory** |
| **Adult Census Parallel Read** | Adult Census | 32,561 | 9.4 ms (6.8 MB) | **3.8 ms** (**5.9 MB**) | ⚡ **2.5x faster** | 📉 **13% less memory** |

### 🗜️ TPC-H LineItem Multi-Codec Serialization Throughput (60,175 rows)

Comparing write throughput across standard columnar compression formats using the source-generated `WriteParquetAsync` API:

| Codec | Compression Level | Write Time | Allocated Memory | Output Characteristics |
|:--- |:---:|:---:|:---:|:--- |
| **Snappy** | Default | **12.4 ms** | **18.6 MB** | High compression speed, standard Parquet default |
| **Zstandard** | Fastest | **11.9 ms** | **17.9 MB** | Fast analytical compression |
| **Zstandard** | Optimal | **18.7 ms** | **18.2 MB** | High compression ratio for archival / storage |
| **Uncompressed** | None | **8.2 ms** | **16.4 MB** | Zero CPU overhead, ideal for IPC / temporary caches |

---

## 🛠️ Running Benchmarks Locally

You can execute the full BenchmarkDotNet suite locally using the .NET CLI:

```bash
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj
```

To run a specific benchmark class or method filter:
```bash
# Run synthetic scaling benchmarks:
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj -- --filter "*Scaling*"

# Run real-world provenanced dataset benchmarks:
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj -- --filter "*Tpch*" "*Census*"
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
