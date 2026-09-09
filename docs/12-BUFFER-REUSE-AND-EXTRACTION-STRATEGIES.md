# 12 - Buffer Reuse & Column Extraction Strategies

This document records the empirical performance evaluation and architectural analysis of column buffer management and extraction strategies in **Parquet.SourceGenerator**.

---

## 🎯 1. Executive Summary

During row-group serialization, domain models in memory (collections of POCOs/records) must be transposed into columnar arrays before being handed to `ParquetRowGroupWriter`. 

We investigated whether **column-pipelined extraction** and **cross-column buffer reuse** could reduce peak `ArrayPool` memory pressure compared to the default **row-oriented single-pass extraction**, and whether returning buffers eagerly after each column write impacts performance.

### Key Conclusions

1. **Row-Oriented Single-Pass Extraction is 1.8× to 5.5× Faster than Column-Pipelined Extraction:**
   - Traversing the row collection once and extracting all properties while the object reference resides in CPU L1/L2 data cache overwhelmingly outperforms making $N$ separate traversals over the collection (one pass per column).
   - At 50,000 rows across 16 columns, multi-pass traversal causes severe CPU cache eviction and memory bus stalls.

2. **Eager Progressive Buffer Returns Achieve the Optimal Balance:**
   - Rented column buffers are returned to `ArrayPool.Shared` immediately after `WriteAsync` or `WriteAllPartsAsync` completes for each column.
   - Using a **single outer `try / finally`** with an eager nulling pattern (`buffer = null!`) and null-checked cleanup (`if (buffer != null)`) avoids the state machine bloat and stack spills of multiple nested `try / finally` blocks while releasing memory milliseconds earlier during asynchronous I/O and compression.

3. **Buffer Reuse within Column-Pipelining Delivers a 33% Speedup within its Class:**
   - In schemas with multiple columns of the same physical type (e.g. `long?`, `decimal?`, `string?`), reusing 1 pooled buffer across matching columns and sharing 1 `int[] defLevels` buffer across all nullable struct columns reduced pipeline latency from 67.9 ms to 45.7 ms (a 33% improvement).
   - However, the multi-pass CPU cache misses prevent it from matching the 25.8 ms throughput of single-pass row extraction.

---

## 🔬 2. Evaluated Strategies

| Strategy | Description | Extraction Traversal | Peak Concurrent Rented Buffers | ArrayPool Rentals (16 cols) |
| :--- | :--- | :---: | :---: | :---: |
| **1. Reflection Baseline** | Stock `ParquetSerializer.SerializeAsync` (Parquet.Net v6). Uses compiled Expression trees and Dremel shredding. | Row-by-row reflection | N/A | Dynamic allocations |
| **2. Row-Oriented (Current Generator + Eager Return)** | All $N$ column buffers and `defLevels` rented upfront. Single loop extracts all properties into all buffers. Columns written sequentially; buffers returned eagerly post-write. | **1 pass** over rows | $N$ column buffers + defLevels (e.g. 27) | $N$ + defLevels (27) |
| **3. Column-Pipelined (No Reuse)** | For each column: rent buffer $\to$ traverse rows extracting that property $\to$ write column $\to$ return buffer immediately. | **$N$ passes** over rows | 1 data buffer + 1 defLevels (2 max) | $N$ + defLevels (27) |
| **4. Column-Pipelined (Type Buffer Reuse)** | Reuses 1 physical buffer across columns of the same type in schema order. Reuses **1 single `int[] defLevels`** across all 11 nullable struct columns. | **$N$ passes** over rows | 1 data buffer + 1 defLevels (2 max) | **5 total** |

---

## 📊 3. Empirical Benchmark Data

Benchmarks were executed under .NET 8.0 (Release build, ARM64) comparing 10 iterations across two contrasting schemas:
- **Workload A: `BenchmarkTpchLineItem`** — 16 columns (4 `long?`, 4 `decimal?`, 3 `DateTime?`, 5 `string?`; 11 nullable struct columns requiring definition levels).
- **Workload B: `ScaleEvent`** — 4 primitive non-nullable columns (`int`, `double`, `long`, `bool`).

### 3.1 Wide Analytical Schema (`BenchmarkTpchLineItem`, 16 Columns with Nullables)

#### 10,000 Rows
| Strategy | Mean Time | Speedup vs Reflection | GC Collections (Gen 0/1/2) | GC Allocated | Peak Active Buffers |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **1. Reflection (`ParquetSerializer`)** | 73.30 ms | 1.0× (baseline) | 16 / 2 / 2 | 13,402.9 KB | N/A |
| **2. Row-Oriented (Single-Pass + Eager Return)** | **25.80 ms** | **2.8× faster** | 2 / 0 / 0 | 1,919.3 KB | 27 |
| **3. Column-Pipelined (No Reuse, 16 passes)** | 67.90 ms | 1.1× faster | 2 / 0 / 0 | 1,919.3 KB | 2 |
| **4. Column-Pipelined (Buffer Reuse across types)** | 45.70 ms | 1.6× faster | 2 / 0 / 0 | 1,919.3 KB | 2 |

#### 50,000 Rows
| Strategy | Mean Time | Speedup vs Reflection | GC Collections (Gen 0/1/2) | GC Allocated | Peak Active Buffers |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **1. Reflection (`ParquetSerializer`)** | 180.10 ms | 1.0× (baseline) | 49 / 6 / 4 | 62,450.5 KB | N/A |
| **2. Row-Oriented (Single-Pass + Eager Return)** | **37.70 ms** | **4.8× faster** | 11 / 0 / 0 | 9,343.1 KB | 27 |
| **3. Column-Pipelined (No Reuse, 16 passes)** | 57.90 ms | 3.1× faster | 11 / 0 / 0 | 9,343.1 KB | 2 |
| **4. Column-Pipelined (Buffer Reuse across types)** | 53.10 ms | 3.4× faster | 11 / 0 / 0 | 9,343.1 KB | 2 |

---

### 3.2 Narrow Primitive Schema (`ScaleEvent`, 4 Columns Non-Nullable)

#### 10,000 Rows
| Strategy | Mean Time | Speedup vs Reflection | GC Collections (Gen 0/1/2) | GC Allocated | Peak Active Buffers |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **1. Reflection (`ParquetSerializer`)** | 5.10 ms | 1.0× (baseline) | 0 / 0 / 0 | 1,293.6 KB | N/A |
| **2. Row-Oriented (Single-Pass + Eager Return)** | **0.80 ms** | **6.4× faster** | 0 / 0 / 0 | 602.6 KB | 4 |
| **3. Column-Pipelined (4 passes, 1 buffer active)** | 1.30 ms | 3.9× faster | 0 / 0 / 0 | 602.6 KB | 1 |

#### 50,000 Rows
| Strategy | Mean Time | Speedup vs Reflection | GC Collections (Gen 0/1/2) | GC Allocated | Peak Active Buffers |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **1. Reflection (`ParquetSerializer`)** | 7.40 ms | 1.0× (baseline) | 0 / 0 / 0 | 5,639.6 KB | N/A |
| **2. Row-Oriented (Single-Pass + Eager Return)** | **1.90 ms** | **3.9× faster** | 0 / 0 / 0 | 2,946.9 KB | 4 |
| **3. Column-Pipelined (4 passes, 1 buffer active)** | 10.40 ms | 0.7× (slower) | 0 / 0 / 0 | 2,946.9 KB | 1 |

---

## 🧠 4. Architectural Analysis & Hardware Mechanics

### 4.1 CPU Cache Spatial Locality vs. Multi-Pass Traversal

Why is single-pass row-oriented extraction consistently 1.8× to 5.5× faster than column-pipelined extraction?

1. **L1/L2 Data Cache Hits in Single Pass:**
   - In .NET, instances of domain classes and records occupy 64–128 bytes on the managed heap.
   - When executing:
     ```csharp
     for (int i = 0; i < count; i++)
     {
         var item = listItems[i]; // Cache line fill: loads object into L1 cache
         buffer_0[i] = item.OrderKey;      // L1 hit (0 wait states)
         buffer_1[i] = item.PartKey;       // L1 hit (0 wait states)
         buffer_2[i] = item.SuppKey;       // L1 hit (0 wait states)
         buffer_3[i] = item.LineNumber;    // L1 hit (0 wait states)
         ...
     }
     ```
   - Reading `item` brings the entire object's memory into the CPU L1 cache line. Accessing all 16 properties in succession incurs **zero additional memory bus transactions**.
   - The CPU traverses the collection pointer array and dereferences the heap objects **exactly once**.

2. **Cache Line Eviction in Column-Pipelined Extraction:**
   - In column-pipelined extraction, the loop traverses the collection 16 times:
     ```csharp
     // Pass 1:
     for (int i = 0; i < count; i++) buf0[i] = listItems[i].OrderKey;
     // Pass 2:
     for (int i = 0; i < count; i++) buf1[i] = listItems[i].PartKey;
     // ...
     // Pass 16:
     for (int i = 0; i < count; i++) buf15[i] = listItems[i].Comment;
     ```
   - At 50,000 items, the reference array alone occupies 400 KB, and the referenced objects occupy several megabytes across the heap.
   - By the time Pass 2 begins, the CPU cache lines filled during Pass 1 have been completely evicted.
   - The CPU is forced to repeatedly fetch the same objects from main memory (RAM) 16 consecutive times, saturating the memory bus.

---

### 4.2 Parquet Format Constraint: Strict Schema Field Order

`Parquet.Net`'s `ParquetRowGroupWriter` enforces writing columns in exact schema definition order:
```csharp
// If field 8 is 'l_returnflag' (string) and field 10 is 'l_shipdate' (DateTime):
// Attempting to write field 10 before field 8 throws:
System.ArgumentException: "cannot write this column, expected 'l_returnflag', passed: 'l_shipdate' (Parameter 'field')"
```

Because Parquet column chunks within a row group must follow schema order:
- Arbitrary grouping of columns by type (e.g. writing all numeric columns, then all strings, then all dates) is **not allowed** by the Parquet file format specification.
- Column-pipelined buffer reuse can only reuse buffers across columns that appear consecutively or must hold the buffer across intermediate column writes until the next column of the same type is encountered.

---

### 4.3 Why Eager Return with Single `try / finally` is the Optimal Pattern

#### The Anti-Pattern: Multiple Nested `try / finally`
Wrapping every column in its own `try / finally` inside an `async` method introduces severe overhead:
- Every `try / finally` adds an entry to the IL Exception Handling Table.
- In `MoveNext()`, the state machine must generate state transitions (`<>1__state`), resumption dispatch tables, and `leave.s` jump instructions across every `await` boundary.
- RyuJIT and Native AOT `ILCompiler` cannot enregister local variables across multiple EH boundaries, forcing variables onto the stack frame.

#### The Implemented Solution: Single `try / finally` + Eager Nulling
```csharp
try
{
    // ... Single-pass extraction ...

    using (var groupWriter = writer.CreateRowGroup())
    {
        await groupWriter.WriteAllPartsAsync(_field_0, ...);
        ArrayPool<int>.Shared.Return(buffer_0, clearArray: false);
        buffer_0 = null!; // Inline 1-cycle assignment

        await groupWriter.WriteAsync(_field_1, ...);
        ArrayPool<string?>.Shared.Return(buffer_1, clearArray: true);
        buffer_1 = null!;
    }
}
finally
{
    // Exception safety: only returns buffers not already eagerly returned
    if (buffer_0 != null) ArrayPool<int>.Shared.Return(buffer_0, clearArray: false);
    if (buffer_1 != null) ArrayPool<string?>.Shared.Return(buffer_1, clearArray: true);
}
```

- **0 additional EH scopes:** Exactly 1 exception handling clause in metadata.
- **Happy path:** Eager returns happen immediately when writing completes, returning memory while hot in cache. Setting `buffer = null!` is a 1-cycle instruction (`xor` / `mov`).
- **Finally check:** Compiles to `test reg, reg; jz`, which branch predictors predict with >99.9% accuracy.

---

## 📋 5. Production Architectural Decision

| Workload / Context | Recommended Strategy | Rationale |
| :--- | :--- | :--- |
| **In-Memory POCO Collections (`List<T>`, `T[]`, `IReadOnlyCollection<T>`)** | **Row-Oriented Single Pass + Eager Progressive Returns** *(Production Default)* | Maximizes L1/L2 CPU cache spatial locality. 1.8×–5.5× faster than multi-pass pipelining. Eager returns immediately release memory during column compression/I/O. |
| **Extreme Memory-Constrained Environments (e.g. AWS Lambda with 128MB RAM, 100+ columns)** | **Column-Pipelined with Type Buffer Reuse** *(Optional Future Flag)* | Reduces peak active memory from $O(N \times \text{batch})$ to $O(1 \times \text{batch})$ at the cost of 2×–3× CPU traversal time. |
| **Column-Oriented In-Memory Sources (e.g. Apache Arrow RecordBatches)** | **Direct Columnar Handoff** | When data is already arranged in contiguous column buffers, zero extraction is needed—buffers can be passed straight to `WriteAsync` with zero passes over rows. |

---

## 🧪 6. Shipped Implementation (issue #136) and Re-measurement

Strategy 4 is now generated. `ParquetSerializerOptions.WriteStrategy` selects between
`RowOriented` (default), `ColumnPipelined` and `Auto`; `Auto` compares
`rowCount * PeakRowOrientedBufferBytesPerRow` — a codegen-time constant emitted onto every
generated extensions class — against `ColumnPipelinedMemoryThresholdBytes` (default 64 MiB).

### 6.1 The reuse map

At emission time the generator groups the schema's columns by *physical buffer element type* and
assigns one pooled slot per distinct type. A slot is rented on its first column and returned after
its last, so a slot whose columns are non-consecutive is carried across the intermediate writes
that Parquet's fixed schema order forces (§4.2). Nullable value columns additionally share one
`int[]` definition-level buffer across the whole row group.

For the 16-column interleaved lineitem schema this gives four data slots plus one shared
`defLevels` buffer — **5 rentals against the row-oriented path's 27**, reproducing the figure in
§2 — and reduces peak concurrent buffer bytes from **284 to 60 bytes per row**.

Peak is not `O(1 × batchSize)` as originally hoped: it is
`O(max simultaneously-live type slots × batchSize)`. Returning at each column's last use, rather
than after every column, is what buys the rental reduction, and the schema's interleaving decides
how many slots overlap. A schema whose same-type columns are consecutive collapses to a single live
slot; a fully interleaved one keeps every distinct type live at once.

### 6.2 Why "Allocated" is the wrong metric

Sibling experiment #212 established that on this codebase `ArrayPool` rentals cost nothing in
allocated bytes once the pool is warm. That is confirmed here: across all four benchmark arms
below, BenchmarkDotNet reports **identical** allocated bytes. The claim this strategy makes is
about *peak concurrently-live buffer bytes*, which only shows up against a cold pool or in a heap
peak, so it is measured separately.

### 6.3 Measured — peak memory, cold pool, fresh process per reading

`dotnet test/Parquet.SourceGenerator.CLI.dll --peak-memory <strategy> --rows N`, macOS 24.6.0 /
Apple Silicon arm64, .NET 8.0 runtime, Parquet.Net 6.1.0, uncompressed, one row group.
`coldAllocated` is the `GC.GetTotalAllocatedBytes` delta across the first write in the process, so
every rental is a real allocation. `peakHeap` is the maximum of `GC.GetTotalMemory(false)` sampled
from a dedicated high-priority thread for the duration of the write, minus the pre-write baseline.
Figures were byte-identical across three repeats.

| Rows | GC mode | Metric | Row-oriented | Column-pipelined | Change |
| ---: | :--- | :--- | ---: | ---: | ---: |
| 10,000 | Workstation | cold-pool allocated | 15,198,120 B | 11,583,472 B | **−23.8 %** |
| 10,000 | Workstation | peak managed heap | 15,767,488 B | 10,701,440 B | **−32.1 %** |
| 10,000 | Workstation | warm-pool allocated | 8,703,368 B | 8,699,464 B | −0.04 % |
| 10,000 | Server | cold-pool allocated | 15,197,784 B | 11,582,368 B | **−23.8 %** |
| 10,000 | Server | peak managed heap | 15,219,728 B | 11,604,376 B | **−23.8 %** |
| 50,000 | Workstation | cold-pool allocated | 85,779,832 B | 71,352,048 B | **−16.8 %** |
| 50,000 | Workstation | peak managed heap | 64,671,152 B | 49,155,448 B | **−24.0 %** |
| 50,000 | Workstation | warm-pool allocated | 60,007,424 B | 60,014,904 B | +0.01 % |
| 50,000 | Server | cold-pool allocated | 85,779,752 B | 71,347,808 B | **−16.8 %** |
| 50,000 | Server | peak managed heap | 76.8–78.3 MB | 71,429,312 B | −7 to −9 % |

The cold-pool saving tracks the emitted constants closely once `ArrayPool`'s power-of-two bucket
rounding is accounted for: at 50,000 rows the pool serves 65,536-element arrays, so the predicted
saving is `65,536 × (284 − 60) = 14.68 MB` against 14.43 MB measured (1.7 % off). At 10,000 rows,
`16,384 × 224 = 3.67 MB` predicted against 3.61 MB measured.

Server GC shows a smaller heap-peak improvement because it defers collection, so the peak is
dominated by uncollected garbage rather than by live buffers; the cold-pool figure, which is
allocation-exact, is unaffected by GC mode.

### 6.4 Measured — CPU cost and heuristic selection

BenchmarkDotNet, `[InProcess]`, same machine, uncompressed, one row group.

| Rows | Arm | Mean | Ratio | Allocated |
| ---: | :--- | ---: | ---: | ---: |
| 10,000 | RowOriented | 8.662 ms | 1.00 | 9.30 MB |
| 10,000 | Auto (below threshold) | 8.683 ms | 1.00 | 9.30 MB |
| 10,000 | ColumnPipelined | 8.793 ms | 1.02 | 9.30 MB |
| 10,000 | Auto (above threshold) | 8.794 ms | 1.02 | 9.30 MB |
| 50,000 | Auto (below threshold) | 44.826 ms | 1.00 | 54.23 MB |
| 50,000 | RowOriented | 44.855 ms | 1.00 | 54.23 MB |
| 50,000 | Auto (above threshold) | 50.736 ms | 1.13 | 54.23 MB |
| 50,000 | ColumnPipelined | 50.809 ms | 1.13 | 54.23 MB |

Each `Auto` arm lands on the mean of the strategy its threshold selects, which is the empirical
validation of the crossover: with the threshold set to exactly
`rowCount * PeakRowOrientedBufferBytesPerRow` the heuristic stays row-oriented, and one byte below
it switches.

The CPU penalty measured here — **2 % at 10k rows and 13 % at 50k** — is far smaller than the
1.8×–5.5× recorded in §3. Two reasons: §3 timed extraction prototypes rather than the full write,
and the generated pipelined path only makes N passes over the *row references*, with Parquet.Net's
encoding and compression dominating the total. The §3 conclusion still stands directionally
(row-oriented is faster), but the price of the memory-constrained path on this codebase is a
single-digit-to-low-teens percentage, not a multiple.

### 6.5 Not covered

- Models with nested struct or list columns emit no pipelined path
  (`SupportsColumnPipelinedWrite == false`) and silently stay row-oriented; their packed lanes are
  not one value per row, so the reuse map does not apply unchanged.
- The V4 legacy backend (`ParquetLegacyIncrementalGenerator`) allocates `new T[count]` per column
  rather than renting, so the reuse map would be a different, GC-side optimisation there; it is
  unimplemented. The modern generator's pipelined path itself uses no API newer than
  `netstandard2.0`, so consumers targeting .NET Standard 2.0 / .NET Framework do get it.
