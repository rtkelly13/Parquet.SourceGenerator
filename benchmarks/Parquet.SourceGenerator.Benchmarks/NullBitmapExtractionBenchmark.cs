using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Parquet.SourceGenerator.Benchmarks;

/// <summary>
/// Row shape for the null-bitmap extraction microbenchmark: a reference-typed record, matching
/// what <c>[ParquetSerializable]</c> models normally are, so the extraction loop pays the same
/// dependent-load cost the generated code pays.
/// </summary>
public sealed record NullableRow(int Row, int? Optional);

/// <summary>
/// Isolates the cost of building Parquet definition levels plus a packed non-null value buffer
/// for one nullable column (issue #145). No Parquet I/O, no allocation inside the measured
/// region: this is the extraction routine on its own, so a win or loss here cannot be masked by
/// GC or by the compressor.
/// </summary>
[MemoryDiagnoser]
[InProcess]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.Declared)]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class NullBitmapExtractionBenchmark
{
    private const int Count = 100_000;

    private NullableRow[] _rows = null!;
    private int?[] _column = null!;
    private int[] _defLevels = null!;
    private int[] _values = null!;
    private byte[] _presence = null!;

    /// <summary>Percentage of slots that are null.</summary>
    [Params(0, 10, 20, 50, 90, 100)]
    public int NullPercent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Deterministic pseudo-random null placement: an unpredictable pattern is exactly the
        // case the branchy form mispredicts on, and a fixed seed keeps runs comparable.
        var rng = new Random(20250909);
        _rows = new NullableRow[Count];
        _column = new int?[Count];
        for (int i = 0; i < Count; i++)
        {
            bool present = rng.Next(100) >= NullPercent;
            int? v = present ? i * 7919 : null;
            _rows[i] = new NullableRow(i, v);
            _column[i] = v;
        }

        _defLevels = new int[Count];
        _values = new int[Count];
        _presence = new byte[Count];
    }

    // ── array-of-structures loops: the two shapes the generator can actually emit ──

    /// <summary>The pre-#145 emitted shape: a conditional per row.</summary>
    [Benchmark(Baseline = true)]
    public int Aos_Branchy()
    {
        ref NullableRow srcRef = ref MemoryMarshal.GetArrayDataReference(_rows);
        ref int dstRef = ref MemoryMarshal.GetArrayDataReference(_values);
        ref int defRef = ref MemoryMarshal.GetArrayDataReference(_defLevels);
        int nonNullCount = 0;
        for (int i = 0; i < Count; i++)
        {
            int? val = Unsafe.Add(ref srcRef, i).Optional;
            if (val.HasValue)
            {
                Unsafe.Add(ref dstRef, nonNullCount++) = val.Value;
                Unsafe.Add(ref defRef, i) = 1;
            }
            else
            {
                Unsafe.Add(ref defRef, i) = 0;
            }
        }

        return nonNullCount;
    }

    /// <summary>The post-#145 emitted shape: unconditional store, cursor advanced by the flag.</summary>
    [Benchmark]
    public int Aos_Branchless()
    {
        ref NullableRow srcRef = ref MemoryMarshal.GetArrayDataReference(_rows);
        ref int dstRef = ref MemoryMarshal.GetArrayDataReference(_values);
        ref int defRef = ref MemoryMarshal.GetArrayDataReference(_defLevels);
        int nonNullCount = 0;
        for (int i = 0; i < Count; i++)
        {
            int? val = Unsafe.Add(ref srcRef, i).Optional;
            bool has = val.HasValue;
            int hv = Unsafe.As<bool, byte>(ref has);
            Unsafe.Add(ref dstRef, nonNullCount) = val.GetValueOrDefault();
            nonNullCount += hv;
            Unsafe.Add(ref defRef, i) = hv;
        }

        return nonNullCount;
    }

    // ── contiguous Nullable<T> column: the shape a columnar hand-off would give ──

    [Benchmark]
    public int Columnar_Branchy()
    {
        int nonNullCount = 0;
        int?[] column = _column;
        int[] values = _values;
        int[] def = _defLevels;
        for (int i = 0; i < Count; i++)
        {
            int? val = column[i];
            if (val.HasValue)
            {
                values[nonNullCount++] = val.Value;
                def[i] = 1;
            }
            else
            {
                def[i] = 0;
            }
        }

        return nonNullCount;
    }

    [Benchmark]
    public int Columnar_Branchless() =>
        NullableColumnExtractor.ExtractBranchless<int>(_column, _defLevels, _values);

    [Benchmark]
    public int Columnar_TwoPassSimd() =>
        NullableColumnExtractor.ExtractTwoPass<int>(_column, _presence, _defLevels, _values);
}

/// <summary>
/// A nullable-heavy write model for the end-to-end serialization measurement.
/// </summary>
[ParquetSerializable]
public partial record SparseNullableEvent
{
    [ParquetColumn("Id")]
    public int Id { get; init; }

    [ParquetColumn("OptA")]
    public int? OptA { get; init; }

    [ParquetColumn("OptB")]
    public long? OptB { get; init; }

    [ParquetColumn("OptC")]
    public double? OptC { get; init; }

    [ParquetColumn("OptD")]
    public bool? OptD { get; init; }
}

/// <summary>
/// End-to-end write of a nullable-heavy dataset. The benchmarks project cannot build
/// BenchmarkDotNet's out-of-process boilerplate (it sets <c>ProduceReferenceAssembly=false</c>),
/// so every job here is in-process and the GC mode is selected by running the whole process
/// under <c>DOTNET_gcServer=0</c> and then <c>DOTNET_gcServer=1</c> — PR #209 found the GC mode
/// able to invert a result, so both are reported.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(InProcessConfig))]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.Declared)]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class SparseNullableWriteBenchmark
{
    private sealed class InProcessConfig : ManualConfig
    {
        public InProcessConfig()
        {
            AddJob(
                Job.Default.WithToolchain(InProcessEmitToolchain.Instance)
                    .WithWarmupCount(3)
                    .WithIterationCount(10)
            );
        }
    }

    private List<SparseNullableEvent> _data = null!;

    [Params(20, 50)]
    public int NullPercent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(20250909);
        _data = Enumerable
            .Range(0, 200_000)
            .Select(i =>
            {
                bool present = rng.Next(100) >= NullPercent;
                return new SparseNullableEvent
                {
                    Id = i,
                    OptA = present ? i : null,
                    OptB = present ? (long)i * 31 : null,
                    OptC = present ? i * 0.5 : null,
                    OptD = present ? (i % 2 == 0) : null,
                };
            })
            .ToList();
    }

    [Benchmark]
    public async Task WriteNullableColumns()
    {
        using var stream = new MemoryStream();
        await _data.WriteParquetAsync(stream);
    }
}
