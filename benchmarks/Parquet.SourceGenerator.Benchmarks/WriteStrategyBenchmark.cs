using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace Parquet.SourceGenerator.Benchmarks;

/// <summary>
/// Sixteen columns over four physical buffer types, interleaved so that no two same-type columns
/// are adjacent and every reused slot must be carried across intermediate writes.
/// </summary>
[ParquetSerializable]
public partial record StrategyLineItem
{
    [ParquetColumn("l_orderkey")]
    public long? OrderKey { get; init; }

    [ParquetColumn("l_shipdate")]
    public DateTime? ShipDate { get; init; }

    [ParquetColumn("l_quantity")]
    public decimal? Quantity { get; init; }

    [ParquetColumn("l_comment")]
    public string? Comment { get; init; }

    [ParquetColumn("l_partkey")]
    public long? PartKey { get; init; }

    [ParquetColumn("l_commitdate")]
    public DateTime? CommitDate { get; init; }

    [ParquetColumn("l_extendedprice")]
    public decimal? ExtendedPrice { get; init; }

    [ParquetColumn("l_returnflag")]
    public string? ReturnFlag { get; init; }

    [ParquetColumn("l_suppkey")]
    public long? SuppKey { get; init; }

    [ParquetColumn("l_receiptdate")]
    public DateTime? ReceiptDate { get; init; }

    [ParquetColumn("l_discount")]
    public decimal? Discount { get; init; }

    [ParquetColumn("l_linestatus")]
    public string? LineStatus { get; init; }

    [ParquetColumn("l_linenumber")]
    public long? LineNumber { get; init; }

    [ParquetColumn("l_tax")]
    public decimal? Tax { get; init; }

    [ParquetColumn("l_shipinstruct")]
    public string? ShipInstruct { get; init; }

    [ParquetColumn("l_shipmode")]
    public string? ShipMode { get; init; }
}

/// <summary>
/// CPU cost of the column-pipelined write strategy against the row-oriented default, and the
/// behaviour of the <c>Auto</c> heuristic either side of its threshold.
/// </summary>
/// <remarks>
/// The <c>Allocated</c> column is deliberately <i>not</i> the headline here. With a warm
/// <see cref="System.Buffers.ArrayPool{T}"/> — which is what BenchmarkDotNet measures after its
/// warmup iterations — rentals allocate nothing, so both strategies report the same allocated
/// bytes however many buffers they hold. The memory claim this work makes is about peak
/// concurrently-live buffer bytes, which is measured separately by the CLI's
/// <c>--peak-memory</c> probe against a cold pool in a fresh process. What belongs here is the CPU
/// price of the extra traversals, and the confirmation that <c>Auto</c> lands on the strategy the
/// threshold selects.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class WriteStrategyBenchmark
{
    private List<StrategyLineItem> _data = null!;
    private ParquetSerializerOptions _rowOriented = null!;
    private ParquetSerializerOptions _columnPipelined = null!;
    private ParquetSerializerOptions _autoBelowThreshold = null!;
    private ParquetSerializerOptions _autoAboveThreshold = null!;

    [Params(10_000, 50_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = Enumerable
            .Range(0, Count)
            .Select(i => new StrategyLineItem
            {
                OrderKey = i % 7 == 0 ? null : i,
                ShipDate = i % 5 == 0 ? null : new DateTime(2020, 1, 1).AddDays(i % 900),
                Quantity = i % 3 == 0 ? null : i * 1.5m,
                Comment = i % 11 == 0 ? null : "lorem ipsum dolor sit amet consectetur",
                PartKey = i * 3L,
                CommitDate = new DateTime(2021, 6, 1).AddHours(i % 500),
                ExtendedPrice = i * 12.25m,
                ReturnFlag = i % 2 == 0 ? "A" : "N",
                SuppKey = i % 13 == 0 ? null : i * 7L,
                ReceiptDate = new DateTime(2022, 3, 4).AddMinutes(i % 10000),
                Discount = 0.05m * (i % 5),
                LineStatus = "O",
                LineNumber = i % 4,
                Tax = 0.01m * (i % 9),
                ShipInstruct = i % 17 == 0 ? null : "DELIVER IN PERSON",
                ShipMode = "AIR",
            })
            .ToList();

        // The crossover the heuristic is asked to find: count * PeakRowOrientedBufferBytesPerRow.
        long estimate =
            (long)Count * StrategyLineItemParquetExtensions.PeakRowOrientedBufferBytesPerRow;

        _rowOriented = Options(ParquetWriteStrategy.RowOriented, long.MaxValue);
        _columnPipelined = Options(ParquetWriteStrategy.ColumnPipelined, 0);
        _autoBelowThreshold = Options(ParquetWriteStrategy.Auto, estimate);
        _autoAboveThreshold = Options(ParquetWriteStrategy.Auto, estimate - 1);
    }

    private ParquetSerializerOptions Options(ParquetWriteStrategy strategy, long threshold) =>
        new()
        {
            WriteStrategy = strategy,
            RowGroupSize = Count,
            CompressionMethod = ParquetCompressionMethod.None,
            ColumnPipelinedMemoryThresholdBytes = threshold,
        };

    private async Task<long> WriteAsync(ParquetSerializerOptions options)
    {
        using var stream = new MemoryStream(capacity: 1 << 22);
        await _data.WriteParquetAsync(stream, options);
        return stream.Length;
    }

    [Benchmark(Baseline = true)]
    public Task<long> RowOriented() => WriteAsync(_rowOriented);

    [Benchmark]
    public Task<long> ColumnPipelined() => WriteAsync(_columnPipelined);

    [Benchmark]
    public Task<long> Auto_BelowThreshold() => WriteAsync(_autoBelowThreshold);

    [Benchmark]
    public Task<long> Auto_AboveThreshold() => WriteAsync(_autoAboveThreshold);
}
