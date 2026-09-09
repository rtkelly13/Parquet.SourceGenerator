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

## 🔀 Overlapped Row-Group Prefetching (#146, 2026-09-09)

`PrefetchStreamingBenchmark`. 500,000 rows, four columns (`int`, `double`, `long`, `string`),
20,000-row row groups (25 row groups). Every method drains the same file through
`ReadParquetStreamAsync` and does the same per-item work; only the source stream and
`PrefetchNextRowGroup` vary. `ConsumerWorkPerItem` is a per-item spin: `0` is a tight loop that does
nothing but read two fields, `50` is a caller that actually computes something.

`SlowStream` is a synthetic latency stream that busy-waits 200 µs per `Read` call — 4,029 reads per
pass, so **806 ms of modelled I/O latency per op**, standing in for network or cold storage.
`WarmFile` is a real `FileStream` over a local file with a warm page cache.

**Machine/runtime:** Apple M1 (8 cores), macOS 15.7.7 (Darwin 24.6.0), 8 GB RAM,
.NET 9.0.17 Arm64 RyuJIT AdvSIMD, BenchmarkDotNet v0.14.0, `InProcess`, Release, host otherwise idle.

### Default (Concurrent Workstation GC)

| Scenario | Consumer work | Sequential | Prefetched | Change |
|:---|---:|---:|---:|---:|
| In-memory buffer | 0 | 49.62 ms | 46.11 ms | **−7%** |
| Warm local file | 0 | 49.55 ms | 45.26 ms | **−9%** |
| 806 ms latency stream | 0 | 853.41 ms | 852.44 ms | 0% |
| In-memory buffer | 50 | 99.23 ms | 104.03 ms | **+5% (slower)** |
| Warm local file | 50 | 101.55 ms | 103.83 ms | **+2% (slower)** |
| 806 ms latency stream | 50 | 905.68 ms | 848.73 ms | **−6%** |

### Same benchmark under Server GC (`DOTNET_gcServer=1`)

| Scenario | Consumer work | Sequential | Prefetched | Change |
|:---|---:|---:|---:|---:|
| In-memory buffer | 0 | 34.40 ms | 29.40 ms | **−15%** |
| In-memory buffer | 50 | 89.19 ms | 72.10 ms | **−19%** |

### What the numbers say

1. **Prefetching hides the consumer, not the I/O.** A single `Stream` cannot be read concurrently
   with itself, so the 806 ms of modelled latency is still paid serially: with a do-nothing consumer
   the slow-stream case is a dead heat (853 → 852 ms). Give the same slow stream a consumer that
   does work and the whole 57 ms of that work disappears into the latency (906 → 849 ms). What
   overlaps is *caller work against reader work*, which is exactly the mechanism #146 described,
   but it means the win is bounded by `min(decode time, consumer time)` and not by I/O latency.
2. **The warm-cache regression is a GC artefact, not a pipeline cost.** Under the default
   Concurrent Workstation GC, prefetching a busy consumer's row groups is 2–5% *slower*, and the
   allocation columns say why: identical bytes allocated (87.14 MB either way) but 3.6× the Gen2
   collections (1,200 vs 333 per 11 ops), because the run-ahead row group survives long enough to
   be promoted. Flip to Server GC on the same machine, same benchmark, and the regression turns into
   a 19% win. This is the same GC-bound behaviour PR #204 hit on this 8 GB host.
3. **Hence the default is off.** `PrefetchNextRowGroup` is opt-in: there is no configuration in
   which it is free, and at least one common one (workstation GC, warm file, busy consumer) where it
   costs a few percent.

Reproduce with:

```bash
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj -- --filter "*PrefetchStreamingBenchmark*"
DOTNET_gcServer=1 dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj -- --filter "*PrefetchStreamingBenchmark.Memory*"
```

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
