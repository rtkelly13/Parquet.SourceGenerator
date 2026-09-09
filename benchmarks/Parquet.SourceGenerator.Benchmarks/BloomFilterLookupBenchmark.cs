using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace Parquet.SourceGenerator.Benchmarks;

[ParquetSerializable]
public partial record LookupEvent
{
    [ParquetColumn("event_id", BloomFilter = true)]
    public Guid EventId { get; init; }

    [ParquetColumn("customer")]
    public string Customer { get; init; } = string.Empty;

    [ParquetColumn("amount")]
    public double Amount { get; init; }

    [ParquetColumn("sequence")]
    public long Sequence { get; init; }
}

/// <summary>
/// Measures what a split-block Bloom filter actually buys a point lookup on a high-cardinality
/// Guid column (issue #152), against the only alternative the library had: read the whole file and
/// scan it.
/// </summary>
/// <remarks>
/// The Guid keys are uniformly distributed, so every row group's Min/Max interval spans nearly the
/// whole domain and statistics-based skipping cannot eliminate anything — this is precisely the
/// shape the issue is about. The worst case for the filter is a hit in the last row group, which is
/// what <c>Late</c> measures; <c>Missing</c> is the case where no row group holds the value at all.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class BloomFilterLookupBenchmark
{
    private const int RowCount = 200_000;

    private byte[] _plain = null!;
    private byte[] _withFilters = null!;
    private Guid _lateKey;
    private Guid _missingKey;

    [Params(2_000, 10_000)]
    public int RowGroupSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rows = new List<LookupEvent>(RowCount);
        var random = new Random(20250152);
        var buffer = new byte[16];
        for (int i = 0; i < RowCount; i++)
        {
            random.NextBytes(buffer);
            rows.Add(
                new LookupEvent
                {
                    EventId = new Guid(buffer),
                    Customer = "customer-" + (i % 5_000),
                    Amount = i * 1.5,
                    Sequence = i,
                }
            );
        }

        _lateKey = rows[RowCount - 5].EventId;
        _missingKey = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        using var stream = new MemoryStream();
        rows.WriteParquetBatchedAsync(stream, RowGroupSize).GetAwaiter().GetResult();
        _plain = stream.ToArray();

        _withFilters = rows.WriteParquetWithBloomFiltersAsync(RowGroupSize)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Baseline: no filters, so every row group is read and materialized.</summary>
    [Benchmark(Baseline = true)]
    public async Task<LookupEvent?> FullScan_Late()
    {
        List<LookupEvent> all = await LookupEventParquetExtensions.ReadParquetAsync(
            new ReadOnlyMemory<byte>(_plain)
        );
        return all.FirstOrDefault(e => e.EventId == _lateKey);
    }

    [Benchmark]
    public async Task<LookupEvent?> FullScan_Missing()
    {
        List<LookupEvent> all = await LookupEventParquetExtensions.ReadParquetAsync(
            new ReadOnlyMemory<byte>(_plain)
        );
        return all.FirstOrDefault(e => e.EventId == _missingKey);
    }

    [Benchmark]
    public async Task<LookupEvent?> BloomProbe_Late() =>
        await LookupEventParquetExtensions.FindByEventIdAsync(_withFilters, _lateKey);

    [Benchmark]
    public async Task<LookupEvent?> BloomProbe_Missing() =>
        await LookupEventParquetExtensions.FindByEventIdAsync(_withFilters, _missingKey);
}
