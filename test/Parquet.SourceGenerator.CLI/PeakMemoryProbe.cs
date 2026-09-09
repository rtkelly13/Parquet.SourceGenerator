using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Parquet.SourceGenerator.CLI;

/// <summary>
/// Sixteen columns over four physical buffer types, interleaved so no two same-type columns are
/// adjacent — the shape from <c>docs/12-BUFFER-REUSE-AND-EXTRACTION-STRATEGIES.md</c>.
/// </summary>
[ParquetSerializable]
public sealed partial record PeakProbeLineItem
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
/// Measures what the column-pipelined write strategy is actually for: peak concurrently-live
/// column buffer bytes, not steady-state allocation rate.
/// </summary>
/// <remarks>
/// <para>
/// BenchmarkDotNet's <c>Allocated</c> column is the wrong instrument here. Once
/// <see cref="System.Buffers.ArrayPool{T}"/> is warm, a rental allocates nothing, so a warm-pool
/// benchmark reports the same allocated bytes for both strategies even when one holds five buffers
/// and the other twenty-seven. This probe therefore runs each strategy exactly once in a freshly
/// started process, against a cold pool, and reports:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Cold-pool allocated bytes</b> — with an empty pool every rental is a real allocation, so the
/// delta in <c>GC.GetTotalAllocatedBytes</c> across the first write is the buffer memory the
/// process actually had to commit. This is the memory-constrained cold-start case the issue is
/// about (AWS Lambda, Azure Functions Consumption).
/// </description></item>
/// <item><description>
/// <b>Peak managed heap</b> — <c>GC.GetTotalMemory(false)</c> sampled from a background thread for
/// the duration of the write, maximum minus the pre-write baseline.
/// </description></item>
/// <item><description>
/// <b>Warm-pool allocated bytes</b> — the same write repeated with a warm pool, which is expected
/// to be near-identical between strategies and is reported to make that explicit rather than let it
/// look like a null result.
/// </description></item>
/// </list>
/// <para>
/// Run with <c>--peak-memory &lt;rowOriented|columnPipelined&gt; [rows]</c>. Set
/// <c>DOTNET_gcServer=1</c> to take the Server GC reading.
/// </para>
/// </remarks>
internal static class PeakMemoryProbe
{
    public static async Task ExecuteAsync(string strategyName, int rows)
    {
        ParquetWriteStrategy strategy = strategyName.Equals(
            "columnPipelined",
            StringComparison.OrdinalIgnoreCase
        )
            ? ParquetWriteStrategy.ColumnPipelined
            : ParquetWriteStrategy.RowOriented;

        List<PeakProbeLineItem> items = BuildRows(rows);
        var options = new ParquetSerializerOptions
        {
            WriteStrategy = strategy,
            RowGroupSize = rows,
            CompressionMethod = ParquetCompressionMethod.None,
        };

        // Settle the source data and the JIT for everything except the write itself.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        long baselineHeap = GC.GetTotalMemory(forceFullCollection: false);
        long peakHeap = baselineHeap;
        using var sampling = new CancellationTokenSource();
        var sampler = new Thread(() =>
        {
            while (!sampling.IsCancellationRequested)
            {
                long now = GC.GetTotalMemory(forceFullCollection: false);
                if (now > peakHeap)
                    Interlocked.Exchange(ref peakHeap, now);
                Thread.SpinWait(64);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
        sampler.Start();

        long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        using (var stream = new MemoryStream(capacity: 1 << 20))
        {
            await items.WriteParquetAsync(stream, options);
        }
        sw.Stop();
        long coldAllocated = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

        sampling.Cancel();
        sampler.Join();

        // Warm-pool repeat: the pool now holds every buffer this strategy needs.
        long warmAllocated = 0;
        double warmMs = 0;
        for (int i = 0; i < 3; i++)
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            var warmSw = Stopwatch.StartNew();
            using var stream = new MemoryStream(capacity: 1 << 20);
            await items.WriteParquetAsync(stream, options);
            warmSw.Stop();
            warmAllocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            warmMs = warmSw.Elapsed.TotalMilliseconds;
        }

        var culture = CultureInfo.InvariantCulture;
        Console.WriteLine(
            string.Join(
                ",",
                $"strategy={strategy}",
                $"rows={rows}",
                $"serverGc={System.Runtime.GCSettings.IsServerGC}",
                $"coldAllocatedBytes={coldAllocated.ToString(culture)}",
                $"peakHeapDeltaBytes={(peakHeap - baselineHeap).ToString(culture)}",
                $"warmAllocatedBytes={warmAllocated.ToString(culture)}",
                $"coldMs={sw.Elapsed.TotalMilliseconds.ToString("F2", culture)}",
                $"warmMs={warmMs.ToString("F2", culture)}",
                $"peakRowOrientedBytesPerRow={PeakProbeLineItemParquetExtensions.PeakRowOrientedBufferBytesPerRow}",
                $"peakColumnPipelinedBytesPerRow={PeakProbeLineItemParquetExtensions.PeakColumnPipelinedBufferBytesPerRow}",
                $"rowOrientedRentals={PeakProbeLineItemParquetExtensions.RowOrientedBufferRentals}",
                $"columnPipelinedRentals={PeakProbeLineItemParquetExtensions.ColumnPipelinedBufferRentals}",
                // PeakWorkingSet64 is not populated on macOS, so report the runtime's own
                // committed-bytes figure instead.
                $"gcCommittedBytes={GC.GetGCMemoryInfo().TotalCommittedBytes.ToString(culture)}"
            )
        );
    }

    private static List<PeakProbeLineItem> BuildRows(int count) =>
        Enumerable
            .Range(0, count)
            .Select(i => new PeakProbeLineItem
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
}
