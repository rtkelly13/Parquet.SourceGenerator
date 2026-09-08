using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Apache.Arrow;
using BenchmarkDotNet.Attributes;

namespace Parquet.SourceGenerator.Benchmarks;

/// <summary>
/// The OrderEvent shape used across the docs and the golden suite, carried into the benchmarks so
/// the Arrow export row (#178) is comparable with the POCO read row on the same columns.
/// </summary>
[ParquetSerializable]
public partial record ArrowOrderEvent
{
    /// <summary>Row identity.</summary>
    [ParquetColumn("id")]
    public int Id { get; init; }

    /// <summary>A nullable utf8 column — the offsets-plus-validity path.</summary>
    [ParquetColumn("name")]
    public string? Name { get; init; }

    /// <summary>A fixed-width column that copies as a whole span.</summary>
    [ParquetColumn("score")]
    public double Score { get; init; }

    /// <summary>Decimal128, precision and scale off the schema.</summary>
    [ParquetColumn("price")]
    [ParquetDecimal(18, 4)]
    public decimal Price { get; init; }

    /// <summary>Microsecond timestamp.</summary>
    [ParquetColumn("created_at")]
    [ParquetTimestamp(ParquetTimestampUnit.Microseconds)]
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// RecordBatch export versus a <c>List&lt;POCO&gt;</c> read over the same file. Both decode the same
/// columns through the same rented buffers; the difference is what they materialise — row objects
/// with per-row field writes, or Arrow-owned columnar buffers.
/// </summary>
[MemoryDiagnoser]
[InProcess]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class ArrowExportBenchmark
{
    private byte[] _parquetBytes = null!;

    /// <summary>Row count of the file under test.</summary>
    [Params(10_000, 100_000)]
    public int Count { get; set; }

    /// <summary>Writes the file once, outside the measured region.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        List<ArrowOrderEvent> data = Enumerable
            .Range(0, Count)
            .Select(i => new ArrowOrderEvent
            {
                Id = i,
                Name = i % 7 == 0 ? null : $"order-{i}",
                Score = i * 1.5,
                Price = new decimal(i) + 0.1234m,
                CreatedAt = epoch.AddMilliseconds(i),
            })
            .ToList();

        using var stream = new MemoryStream();
        data.WriteParquetBatchedAsync(stream, rowGroupSize: 20_000).GetAwaiter().GetResult();
        _parquetBytes = stream.ToArray();
    }

    /// <summary>Baseline: materialise every row as a POCO.</summary>
    [Benchmark(Baseline = true)]
    public async Task<int> PocoListRead()
    {
        using var stream = new MemoryStream(_parquetBytes, writable: false);
        List<ArrowOrderEvent> rows = await ArrowOrderEventParquetExtensions.ReadParquetAsync(
            stream
        );
        return rows.Count;
    }

    /// <summary>One RecordBatch per row group, no row objects.</summary>
    [Benchmark]
    public async Task<int> ArrowRecordBatchExport()
    {
        using var stream = new MemoryStream(_parquetBytes, writable: false);
        int rows = 0;
        await foreach (
            RecordBatch batch in ArrowOrderEventParquetExtensions.ReadParquetRecordBatchesAsync(
                stream
            )
        )
        {
            rows += batch.Length;
            batch.Dispose();
        }

        return rows;
    }

    /// <summary>Export split to a fixed batch size, the shape an IPC/Flight consumer asks for.</summary>
    [Benchmark]
    public async Task<int> ArrowRecordBatchExportChunked()
    {
        using var stream = new MemoryStream(_parquetBytes, writable: false);
        int rows = 0;
        await foreach (
            RecordBatch batch in ArrowOrderEventParquetExtensions.ReadParquetRecordBatchesAsync(
                stream,
                maxRowsPerBatch: 4_096
            )
        )
        {
            rows += batch.Length;
            batch.Dispose();
        }

        return rows;
    }
}
