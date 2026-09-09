using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace Parquet.SourceGenerator.Benchmarks;

/// <summary>
/// A row shaped like the analytical tables issue #150 targets: columns declared nullable for
/// business reasons whose individual row groups very often contain no nulls at all.
/// </summary>
[ParquetSerializable]
public partial record NullableScaleEvent
{
    [ParquetColumn("Id")]
    public int Id { get; init; }

    [ParquetColumn("OptInt")]
    public int? OptInt { get; init; }

    [ParquetColumn("OptLong")]
    public long? OptLong { get; init; }

    [ParquetColumn("OptDouble")]
    public double? OptDouble { get; init; }

    [ParquetColumn("OptTimestamp")]
    public DateTime? OptTimestamp { get; init; }
}

/// <summary>
/// Issue #150: reading nullable columns whose chunk statistics report <c>NullCount == 0</c>
/// against the same schema carrying real nulls. Both cases run the same generated reader, so the
/// spread between them is the value of the zero-null fast path; comparing a run of this file
/// before and after the emitter change gives the absolute before/after.
/// </summary>
[MemoryDiagnoser]
[InProcess]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class NullableColumnReadBenchmark
{
    private byte[] _zeroNullBytes = null!;
    private byte[] _mixedNullBytes = null!;

    [Params(200_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _zeroNullBytes = Write(Build(nullEvery: 0));
        _mixedNullBytes = Write(Build(nullEvery: 3));
    }

    private List<NullableScaleEvent> Build(int nullEvery)
    {
        var epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable
            .Range(0, Count)
            .Select(i =>
            {
                bool isNull = nullEvery > 0 && i % nullEvery == 0;
                return new NullableScaleEvent
                {
                    Id = i,
                    OptInt = isNull ? null : i,
                    OptLong = isNull ? null : i * 1000L,
                    OptDouble = isNull ? null : i * 3.14159,
                    OptTimestamp = isNull ? null : epoch.AddSeconds(i),
                };
            })
            .ToList();
    }

    private static byte[] Write(List<NullableScaleEvent> data)
    {
        using var stream = new MemoryStream();
        data.WriteParquetBatchedAsync(stream, rowGroupSize: 20_000).GetAwaiter().GetResult();
        return stream.ToArray();
    }

    /// <summary>
    /// Every chunk reports <c>NullCount == 0</c>: the generated reader takes the dense lane.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<List<NullableScaleEvent>> ReadZeroNullColumns()
    {
        using var stream = new MemoryStream(_zeroNullBytes);
        return await NullableScaleEventParquetExtensions.ReadParquetAsync(stream);
    }

    /// <summary>
    /// One row in three is null, so no chunk qualifies and the standard nullable decode runs.
    /// </summary>
    [Benchmark]
    public async Task<List<NullableScaleEvent>> ReadMixedNullColumns()
    {
        using var stream = new MemoryStream(_mixedNullBytes);
        return await NullableScaleEventParquetExtensions.ReadParquetAsync(stream);
    }

    /// <summary>
    /// The array-materialising path over the same zero-null data.
    /// </summary>
    [Benchmark]
    public async Task<NullableScaleEvent[]> ReadZeroNullColumnsArray()
    {
        using var stream = new MemoryStream(_zeroNullBytes);
        return await NullableScaleEventParquetExtensions.ReadParquetArrayAsync(stream);
    }

    /// <summary>
    /// The array-materialising path over the mixed-null data, for the same comparison.
    /// </summary>
    [Benchmark]
    public async Task<NullableScaleEvent[]> ReadMixedNullColumnsArray()
    {
        using var stream = new MemoryStream(_mixedNullBytes);
        return await NullableScaleEventParquetExtensions.ReadParquetArrayAsync(stream);
    }
}
