# 06 - Test Data Specification & Symmetrical Benchmarking

## Purpose & Symmetrical Property Verification

To prove the correctness, precision, and performance of `Parquet.SourceGenerator` across multiple scales and complexities, we maintain **symmetrically deterministic test datasets**. 

These datasets are independently generated across multiple major Parquet specification and library versions:
1. **Python Engine (`PyArrow` via `uv`)**:
   - `test/data/v1/`: Parquet Format Specification v1.0 (legacy format compatibility).
   - `test/data/v2/`: Parquet Format Specification v2.6 (modern format compatibility).
2. **C# Engine (`Parquet.Net` via `dotnet run`)**:
   - `test/data_csharp/v3/`: Generated using `Parquet.Net` v3.x API.
   - `test/data_csharp/v4/`: Generated using `Parquet.Net` v4.x API.

Because row generation uses strict deterministic mathematical formulas based on the zero-indexed row index $i$, any deserializer or serializer in `Parquet.SourceGenerator` can be proven symmetrically valid across all major format and library versions.

---

## 📊 Dataset Matrix & Specifications

### 1. `01_small_flat_primitives.parquet`
- **Scale**: 100 rows
- **Complexity**: Low (flat, non-null primitives)
- **Purpose**: Fast smoke testing and quick unit test assertions.

| Field Name | Type | Deterministic Row Formula ($i \in [0, 99]$) |
| :--- | :--- | :--- |
| `id` | `INT32` | $i$ |
| `name` | `UTF8` | `"user_" + i` |
| `score` | `DOUBLE` | $(i \times 1.5) \pmod{100.0}$ |
| `is_active` | `BOOLEAN` | $i \pmod 2 == 0$ |
| `created_at_ms` | `INT64` | $1700000000000 + (i \times 1000)$ |

---

### 2. `02_medium_nullable_types.parquet`
- **Scale**: 10,000 rows
- **Complexity**: Medium (nullable primitives with null definition levels)
- **Purpose**: Verifies null bitmap handling and nullable types (`int?`, `double?`, `string?`, `bool?`).

| Field Name | Type | Deterministic Null Rule ($i \in [0, 9999]$) | Deterministic Value Formula |
| :--- | :--- | :--- | :--- |
| `id` | `INT32` | Non-null | $i$ |
| `nullable_int` | `INT32?` | `NULL` if $i \pmod 5 == 0$ | $i \times 10$ |
| `nullable_double` | `DOUBLE?` | `NULL` if $i \pmod 5 == 0$ | $(i \times 3.14159) \pmod{1000.0}$ |
| `nullable_string` | `UTF8?` | `NULL` if $i \pmod 5 == 0$ | `"str_val_" + i` |
| `nullable_bool` | `BOOLEAN?` | `NULL` if $i \pmod 5 == 0$ | $i \pmod 3 == 0$ |

---

### 3. `03_complex_decimals_guids.parquet`
- **Scale**: 5,000 rows
- **Complexity**: Medium-High (Fixed precision decimals, GUID strings, timestamps)
- **Purpose**: Verifies high-precision data types, byte scaling, and GUID parsing.

| Field Name | Type | Deterministic Row Formula ($i \in [0, 4999]$) |
| :--- | :--- | :--- |
| `id` | `INT32` | $i$ |
| `guid_str` | `UTF8` | `Guid.Parse(i+1, 0, 0, ...)` |
| `amount` | `DECIMAL(18, 4)` | $(i \times 123.4567) \pmod{99999.9999}$ |
| `timestamp_us` | `INT64` / `TIMESTAMP_US` | $1700000000000000 + (i \times 100000)$ |
| `category` | `INT32` | $i \pmod 4$ |

---

### 4. `04_nested_lists_maps.parquet`
- **Scale**: 1,000 rows
- **Complexity**: High (Nested repeated lists and map key-value structures)
- **Purpose**: Verifies repetition levels, list fields (`DataField<List<T>>`), and map fields.

| Field Name | Type | Deterministic Row Value |
| :--- | :--- | :--- |
| `id` | `INT32` | $i$ |
| `tags` | `LIST<UTF8>` | `["primary", "tag_" + (i % 10), "sub_" + (i % 3)]` |
| `scores` | `LIST<INT32>` | `[i, i + 1, i + 2]` |
| `metadata` | `MAP<UTF8, UTF8>` | `{"env": (i % 2 == 0 ? "production" : "staging"), "index": str(i)}` |

---

### 5. `05_large_scale_flat.parquet`
- **Scale**: 100,000 rows
- **Complexity**: High Scale (Volume throughput and GC memory allocation benchmarking)
- **Purpose**: Used by `BenchmarkDotNet` to profile MB/s throughput and allocation overhead.

| Field Name | Type | Deterministic Row Formula ($i \in [0, 99999]$) |
| :--- | :--- | :--- |
| `id` | `INT64` | $i$ |
| `payload` | `UTF8` | `"payload_data_string_buffer_segment_" + (i % 500)` |
| `val_a` | `INT32` | $i \times 7$ |
| `val_b` | `DOUBLE` | $i \times 0.123456789$ |
| `is_valid` | `BOOLEAN` | $i \pmod 7 \neq 0$ |

---

## 🏛️ Git LFS Provenanced Benchmark Datasets

In addition to synthetic unit test fixtures, the repository maintains real-world, fixed public datasets under [`benchmarks/data/`](../benchmarks/data/) tracked via **Git LFS**. Each dataset is cryptographically pinned and accompanied by a machine-readable [`provenance.json`](../benchmarks/data/provenance.json) and human-readable [`PROVENANCE.md`](../benchmarks/data/PROVENANCE.md).

### Registered Public Datasets

1. **`tpch_lineitem_sf001.parquet`** (60,175 rows, 1.34 MB)
   - **Origin**: TPC-H SF 0.01 via Hugging Face (`liangyc/tpch-sf-0_01`, commit `a91e9442ea`)
   - **License**: Apache-2.0
   - **Profile**: 16 columns across 4 `Int64` keys, 4 `Decimal(15,2)` financial amounts, 3 `Date` timestamps, 4 dictionary-encoded strings (`l_returnflag`, `l_linestatus`, `l_shipinstruct`, `l_shipmode`), and 1 plain free-text string (`l_comment`).
2. **`adult_census_income.parquet`** (32,561 rows, 554 KB)
   - **Origin**: Adult Census Income via Hugging Face (`scikit-learn/adult-census-income`, commit `aefa0f0f1b`)
   - **License**: CC-BY-4.0
   - **Profile**: 15 columns with heavy categorical dictionary encoding (9 string columns including `workclass`, `education`, `occupation`, `relationship`, `income`).
3. **`diamonds.parquet`** (53,940 rows, 785 KB)
   - **Origin**: Diamonds regression benchmark via Hugging Face (`inria-soda/tabular-benchmark`, commit `cb2bcee34f`)
   - **License**: CC0-1.0
   - **Profile**: 10 columns testing floating-point precision (`carat`, `depth`, `table`, `x`, `y`, `z`, `price`) and ordinal categories.

---

## 🛠️ Verification & Synchronization Commands

### Standalone C# Provenance Verification (.NET 10 via `dotnet run`)
```bash
dotnet run scripts/VerifyProvenance.cs
```

### Re-fetching & Re-profiling Datasets (.NET 10 via `dotnet run`)
```bash
dotnet run scripts/FetchBenchmarkDatasets.cs
```

### Synthetic Test Datasets (PyArrow Format Compatibility)
The datasets under `test/data/v1/` and `test/data/v2/` are committed deterministic test fixtures generated to guarantee cross-engine format compatibility. Python (`.py`) files are forbidden in the repository; all repository tooling and scripts are authored in C#.

### C# Synthetic Generator (`Parquet.Net` via `dotnet run`)
```bash
dotnet run --project test/Parquet.SourceGenerator.CLI/Parquet.SourceGenerator.CLI.csproj
```

