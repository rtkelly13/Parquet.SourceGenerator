# Performance Benchmarks & Baseline Reports

`Parquet.SourceGenerator` is designed for high-performance C# Parquet serialization and deserialization, achieving zero reflection, Native AOT compatibility, and minimum memory allocation.

---

## ⚡ Headline Performance Baseline

The following baseline metrics compare **`Parquet.SourceGenerator`** against **`ParquetSerializer` v6** (reflection baseline) at **100,000 items** scale:

| Operation | Scale | Reflection Baseline | Source Generator | Speedup | Memory Reduction |
|:--- |:---:|:---:|:---:|:---:|:---:|
| **File Serialization (Write)** | 100,000 items | 7.05 ms (11.00 MB) | **2.91 ms** (**5.74 MB**) | ⚡ **2.4x faster** | 📉 **48% less memory** |
| **Streaming Batched Write** | 100,000 items | 7.05 ms (11.00 MB) | **3.50 ms** (**5.03 MB**) | ⚡ **2.0x faster** | 📉 **54% less memory** |
| **File Deserialization (Read)** | 100,000 items | 9.81 ms (12.30 MB) | **5.57 ms** (**8.99 MB**) | ⚡ **1.8x faster** | 📉 **27% less memory** |
| **Parallel Deserialization (Read)** | 100,000 items | 9.81 ms (12.30 MB) | **5.68 ms** (**9.86 MB**) | ⚡ **1.7x faster** | 📉 **20% less memory** |
| **Streaming Read (IAsyncEnumerable)** | 100,000 items | 9.81 ms (12.30 MB) | **4.13 ms** (**8.22 MB**) | ⚡ **2.4x faster** | 📉 **33% less memory** |
| **Guid Serialization** | 100,000 items | 8.97 ms (17.70 MB) | **6.82 ms** (**10.70 MB**) | ⚡ **1.3x faster** | 📉 **40% less memory** |

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
| **TPC-H LineItem Read** | TPC-H SF 0.01 | 60,175 | 85.46 ms (55.11 MB) | **55.29 ms** (**38.59 MB**) | ⚡ **1.5x faster** | 📉 **30% less memory** |
| **TPC-H Parallel Read (4 Cores)** | TPC-H SF 0.01 | 60,175 | 85.46 ms (55.11 MB) | **58.27 ms** (**39.07 MB**) | ⚡ **1.4x faster** | 📉 **29% less memory** |
| **TPC-H Streaming Read** | TPC-H SF 0.01 | 60,175 | 85.46 ms (55.11 MB) | **44.44 ms** (**38.13 MB**) | ⚡ **1.9x faster** | 📉 **31% less memory** |
| **Adult Census Read (Dictionaries)** | Adult Census | 32,561 | 37.94 ms (29.25 MB) | **28.74 ms** (**20.24 MB**) | ⚡ **1.3x faster** | 📉 **31% less memory** |
| **Adult Census Parallel Read** | Adult Census | 32,561 | 37.94 ms (29.25 MB) | **30.07 ms** (**22.90 MB**) | ⚡ **1.3x faster** | 📉 **22% less memory** |
| **Adult Census Streaming Read** | Adult Census | 32,561 | 37.94 ms (29.25 MB) | **11.49 ms** (**19.99 MB**) | ⚡ **3.3x faster** | 📉 **32% less memory** |
| **Diamonds Read** | Diamonds | 53,940 | 22.09 ms (19.78 MB) | **11.92 ms** (**12.52 MB**) | ⚡ **1.9x faster** | 📉 **37% less memory** |
| **Diamonds Parallel Read** | Diamonds | 53,940 | 22.09 ms (19.78 MB) | **13.63 ms** (**15.65 MB**) | ⚡ **1.6x faster** | 📉 **21% less memory** |
| **Diamonds Streaming Read** | Diamonds | 53,940 | 22.09 ms (19.78 MB) | **7.52 ms** (**12.11 MB**) | ⚡ **2.9x faster** | 📉 **39% less memory** |

### 🗜️ TPC-H LineItem Multi-Codec Serialization Throughput (60,175 rows)

Comparing write throughput across standard columnar compression formats using the source-generated `WriteParquetAsync` API:

| Codec | Compression Level | Write Time | Allocated Memory | Output Characteristics |
|:--- |:---:|:---:|:---:|:--- |
| **Snappy** | Default | **37.85 ms** | **14.89 MB** | High compression speed, standard Parquet default |
| **Zstandard** | Fastest | **48.32 ms** | **22.40 MB** | Fast analytical compression |
| **Zstandard** | Optimal | **49.94 ms** | **23.06 MB** | High compression ratio for archival / storage |
| **Uncompressed** | None | **30.77 ms** | **38.18 MB** | Zero CPU overhead, ideal for IPC / temporary caches |

---

## 📏 Re-baseline (2026-09-08, dev host)

First read-suite measurement since the `List<T>.Capacity` fix (docs/07 §2.2) and the real parallel reader (§2.1), closing the audit's "reasoned rather than measured" note.

**Host:** Apple M1, .NET 9.0.17 (net8 target, roll-forwarded), InProcess, BenchmarkDotNet v0.14.0, Release. Absolute times on this host run **2–3× above the CI baseline** — the reflection baseline included — so this section records *within-run ratios* only. The tables above remain the source of truth for absolute throughput, which CI regenerates.

The gap between bulk `ReadParquetAsync` and streaming `ReadParquetStreamAsync` isolates the row materialization + list growth a batched AoS API (see #147) could remove:

| Dataset | Bulk | Streamed | Materialization share |
|:---|---:|---:|---:|
| ScaleEvent 1M (4 primitive cols) | 131.1 ms | 101.0 ms | ~23% |
| Diamonds 54k | 51.3 ms | 34.3 ms | ~33% |
| TPC-H LineItem 60k | 113.7 ms | 103.7 ms | ~9% |
| Adult Census 33k (dict strings) | 210.6 ms | 109.9 ms | ~48% (Gen2-stressed outlier) |

**Conclusions:**

1. Decode + string handling dominates read time (~60–90%) even on the most struct-friendly schema. The AoS batch-overload idea caps at ~10–30% on bulk reads and stays a parked sub-issue of #147.
2. Parallel buffer read was the outright fastest Census reader (83.5 ms vs 210.6 bulk / 109.9 streamed) and matched the sequential readers on TPC-H and Diamonds — consistent with decode-bound workloads.
3. Read-perf roadmap re-sequenced: #143 (null-bypass paths) and #146 (row-group prefetching) attack the measured hot spot and land ahead of #147's AoS sibling.

Artifacts: `BenchmarkDotNet.Artifacts/results/*-report-github.md` (2026-09-08 run); discussion in [issue #147](https://github.com/rtkelly13/Parquet.SourceGenerator/issues/147).

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

---

## 🌸 Bloom Filter Point Lookups (issue #152)

`BloomFilterLookupBenchmark` measures a point lookup on a high-cardinality `Guid` column — the case
Min/Max statistics cannot skip, because every row group's interval spans the whole domain. The
baseline is the only thing the library could do before: read the whole file and scan it.

**Dataset**: 200,000 rows, `Guid` + `string` + `double` + `long`, uniformly random Guid keys, Snappy.
**Machine**: Apple Silicon (arm64), macOS 24.6.0, .NET 9.0.17, BenchmarkDotNet 0.14.0, `InProcess`.
**Caveat**: an unrelated benchmark was running concurrently on the same machine, which is why the
standard deviations are wide (the two `FullScan` rows do identical work and still differ by ~25% at
`RowGroupSize=2000`). The effect sizes below are one to two orders of magnitude, so the ranking is
not in doubt, but treat the individual means as indicative rather than publishable.

| Row group size | Scenario | Full scan | Bloom probe | Speed-up | Allocated (scan → probe) |
|:---:|:--- |---:|---:|:---:|:--- |
| 2,000 (100 groups) | key in the last row group | 78.7 ms | **1.16 ms** | **~68x** | 46.5 MB → **1.4 MB** |
| 2,000 (100 groups) | key absent | 52.8 ms | **1.03 ms** | **~51x** | 46.5 MB → **1.4 MB** |
| 10,000 (20 groups) | key in the last row group | 63.8 ms | **2.65 ms** | **~24x** | 50.2 MB → **2.8 MB** |
| 10,000 (20 groups) | key absent | 62.5 ms | **0.20 ms** | **~306x** | 50.2 MB → **0.44 MB** |

The shape of the result is what the mechanism predicts: an absent key touches no row group at all and
costs only the footer walk plus one probe per group, while a present key still pays for exactly one
row group's pages. Smaller row groups make the hit cheaper (less data behind the match) and the miss
slightly dearer (more filters to walk).

### Write-side cost

Filters are not free. Measured on the four-column `BloomEvent` model with **three** of its four
columns annotated, at 1% target FPP:

| Row group size | Rows | Without filters | With filters | Growth |
|:---:|---:|---:|---:|:---:|
| 250 | 5,000 | 152,345 B | 184,445 B | +21.1% |
| 10,000 | 40,000 | 1,097,725 B | 1,294,645 B | +17.9% |

Most of that is power-of-two rounding of the block count — 10,000 keys at 1% FPP needs ~12.1 KB and
rounds to 16 KB. Annotating one identifier column rather than three, which is the realistic case,
divides the overhead accordingly.
