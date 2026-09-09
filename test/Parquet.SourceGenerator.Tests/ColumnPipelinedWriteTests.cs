using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Wide interleaved schema modelled on TPC-H lineitem: four physical buffer types
/// (long, decimal, DateTime, ReadOnlyMemory&lt;char&gt;?) spread across sixteen columns so that
/// same-type columns are non-consecutive and every slot must be carried across intermediate
/// writes of other types.
/// </summary>
[ParquetSerializable]
public partial record PipelineWideRow
{
    [ParquetColumn("order_key")]
    public long? OrderKey { get; init; }

    [ParquetColumn("ship_date")]
    public DateTime? ShipDate { get; init; }

    [ParquetColumn("quantity")]
    public decimal? Quantity { get; init; }

    [ParquetColumn("comment")]
    public string? Comment { get; init; }

    [ParquetColumn("part_key")]
    public long? PartKey { get; init; }

    [ParquetColumn("commit_date")]
    public DateTime? CommitDate { get; init; }

    [ParquetColumn("extended_price")]
    public decimal? ExtendedPrice { get; init; }

    [ParquetColumn("return_flag")]
    public string? ReturnFlag { get; init; }

    [ParquetColumn("supp_key")]
    public long? SuppKey { get; init; }

    [ParquetColumn("receipt_date")]
    public DateTime? ReceiptDate { get; init; }

    [ParquetColumn("discount")]
    public decimal? Discount { get; init; }

    [ParquetColumn("line_status")]
    public string? LineStatus { get; init; }

    [ParquetColumn("line_number")]
    public long? LineNumber { get; init; }

    [ParquetColumn("tax")]
    public decimal? Tax { get; init; }

    [ParquetColumn("ship_instruct")]
    public string? ShipInstruct { get; init; }

    [ParquetColumn("ship_mode")]
    public string? ShipMode { get; init; }
}

/// <summary>Two long columns adjacent: the slot's live range spans one intermediate column only.</summary>
[ParquetSerializable]
public partial record PipelineConsecutiveRow
{
    [ParquetColumn("a")]
    public long A { get; init; }

    [ParquetColumn("b")]
    public long B { get; init; }

    [ParquetColumn("c")]
    public double C { get; init; }

    [ParquetColumn("d")]
    public double D { get; init; }
}

/// <summary>The same type multiset as <see cref="PipelineConsecutiveRow"/>, interleaved.</summary>
[ParquetSerializable]
public partial record PipelineInterleavedRow
{
    [ParquetColumn("a")]
    public long A { get; init; }

    [ParquetColumn("c")]
    public double C { get; init; }

    [ParquetColumn("b")]
    public long B { get; init; }

    [ParquetColumn("d")]
    public double D { get; init; }
}

/// <summary>
/// Records the exact order in which the writer touches (row, column), which is what distinguishes
/// the two strategies: row-oriented walks all columns of a row before advancing, column-pipelined
/// walks all rows of a column before advancing.
/// </summary>
[ParquetSerializable]
public partial class PipelineProbeRow
{
    public static List<string> Accesses { get; } = [];
    public static bool Recording { get; set; }
    public static string? ThrowOn { get; set; }

    private readonly int _index;

    public PipelineProbeRow() { }

    public PipelineProbeRow(int index) => _index = index;

    private int Touch(string column)
    {
        if (Recording)
        {
            Accesses.Add($"{_index}.{column}");
            if (ThrowOn == column)
                throw new InvalidOperationException($"probe failure on {column}");
        }
        return _index;
    }

    [ParquetColumn("alpha")]
    public int Alpha
    {
        get => Touch(nameof(Alpha));
        init { }
    }

    [ParquetColumn("beta")]
    public int Beta
    {
        get => Touch(nameof(Beta));
        init { }
    }

    [ParquetColumn("gamma")]
    public int Gamma
    {
        get => Touch(nameof(Gamma));
        init { }
    }
}

/// <summary>
/// Two string columns sharing one reference-bearing pooled slot, followed by a numeric column so
/// the string slot is returned before the row group closes. Parquet.Net itself keeps the most
/// recently written column's values reachable (see UPSTREAM_DEPENDENCY_LIMITATIONS.md), so the
/// trailing int column is what absorbs that, leaving the string slot's hygiene observable.
/// </summary>
[ParquetSerializable]
public partial record PipelineStringRow
{
    [ParquetColumn("first")]
    public string? First { get; init; }

    [ParquetColumn("second")]
    public string? Second { get; init; }

    [ParquetColumn("third")]
    public string? Third { get; init; }

    [ParquetColumn("tail")]
    public int Tail { get; init; }
}

/// <summary>
/// Column-pipelined write path (issue #136): the static type-reuse map, the runtime strategy
/// selection, output equivalence with the row-oriented path, and pooled buffer lifetime.
/// </summary>
[Collection("ColumnPipelinedWrite")]
public sealed class ColumnPipelinedWriteTests
{
    private static List<PipelineWideRow> WideRows(int count) =>
        Enumerable
            .Range(0, count)
            .Select(i => new PipelineWideRow
            {
                OrderKey = i % 7 == 0 ? null : i,
                ShipDate = i % 5 == 0 ? null : new DateTime(2020, 1, 1).AddDays(i % 900),
                Quantity = i % 3 == 0 ? null : i * 1.5m,
                Comment = i % 11 == 0 ? null : $"comment-{i}",
                PartKey = i * 3L,
                CommitDate = new DateTime(2021, 6, 1).AddHours(i % 500),
                ExtendedPrice = i * 12.25m,
                ReturnFlag = i % 2 == 0 ? "A" : "N",
                SuppKey = i % 13 == 0 ? null : i * 7L,
                ReceiptDate = new DateTime(2022, 3, 4).AddMinutes(i),
                Discount = 0.05m * (i % 5),
                LineStatus = "O",
                LineNumber = i % 4,
                Tax = 0.01m * (i % 9),
                ShipInstruct = i % 17 == 0 ? null : "DELIVER IN PERSON",
                ShipMode = "AIR",
            })
            .ToList();

    private static async Task<byte[]> WriteAsync(
        IReadOnlyCollection<PipelineWideRow> rows,
        ParquetWriteStrategy strategy,
        long threshold = 64L * 1024 * 1024
    )
    {
        using var ms = new MemoryStream();
        await rows.WriteParquetAsync(
            ms,
            new ParquetSerializerOptions
            {
                WriteStrategy = strategy,
                ColumnPipelinedMemoryThresholdBytes = threshold,
            }
        );
        return ms.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // ── 1. Static reuse map ────────────────────────────────────────────────

    [Fact]
    public void ReuseMapCollapsesRentalsToOnePerDistinctPhysicalType()
    {
        // 16 columns: 4 long?, 4 decimal?, 3 DateTime?, 5 string?.
        // Nullable structs pack into a non-nullable lane, so the physical data types are
        // long, DateTime, decimal and ReadOnlyMemory<char>? — four slots plus one shared int[]
        // definition-level buffer.
        Assert.True(PipelineWideRowParquetExtensions.SupportsColumnPipelinedWrite);
        Assert.Equal(5, PipelineWideRowParquetExtensions.ColumnPipelinedBufferRentals);

        // Row-oriented rents a data buffer per column plus a defLevels buffer per nullable struct
        // column: 16 + 11 = 27, exactly the figure recorded in docs/12.
        Assert.Equal(27, PipelineWideRowParquetExtensions.RowOrientedBufferRentals);
    }

    [Fact]
    public void ReuseMapReducesPeakConcurrentBufferBytes()
    {
        Assert.True(
            PipelineWideRowParquetExtensions.PeakColumnPipelinedBufferBytesPerRow
                < PipelineWideRowParquetExtensions.PeakRowOrientedBufferBytesPerRow
        );

        // long(8) + DateTime(8) + decimal(16) + ReadOnlyMemory<char>?(24) + defLevels(4) = 60.
        Assert.Equal(60, PipelineWideRowParquetExtensions.PeakColumnPipelinedBufferBytesPerRow);

        // 4*8 + 3*8 + 4*16 + 5*24 + 11*4 = 284.
        Assert.Equal(284, PipelineWideRowParquetExtensions.PeakRowOrientedBufferBytesPerRow);
    }

    [Fact]
    public void ConsecutiveAndInterleavedSchemasRentAlikeButPeakDiffers()
    {
        // Same type multiset (2x long, 2x double), different column order.
        Assert.Equal(
            PipelineConsecutiveRowParquetExtensions.ColumnPipelinedBufferRentals,
            PipelineInterleavedRowParquetExtensions.ColumnPipelinedBufferRentals
        );
        Assert.Equal(2, PipelineConsecutiveRowParquetExtensions.ColumnPipelinedBufferRentals);

        // a,b,c,d — the long slot dies before the double slot is born: peak is one 8-byte slot.
        Assert.Equal(
            8,
            PipelineConsecutiveRowParquetExtensions.PeakColumnPipelinedBufferBytesPerRow
        );

        // a,c,b,d — the long slot must be carried across column c: both slots are live at once.
        Assert.Equal(
            16,
            PipelineInterleavedRowParquetExtensions.PeakColumnPipelinedBufferBytesPerRow
        );

        // Both row-oriented layouts hold all four buffers regardless of order.
        Assert.Equal(32, PipelineConsecutiveRowParquetExtensions.PeakRowOrientedBufferBytesPerRow);
        Assert.Equal(32, PipelineInterleavedRowParquetExtensions.PeakRowOrientedBufferBytesPerRow);
    }

    [Fact]
    public void CompoundModelsReportNoColumnPipelinedPath()
    {
        // NestedOrder carries two Address struct members, so no reuse map is emitted.
        Assert.False(NestedOrderParquetExtensions.SupportsColumnPipelinedWrite);
        Assert.Equal(
            NestedOrderParquetExtensions.PeakRowOrientedBufferBytesPerRow,
            NestedOrderParquetExtensions.PeakColumnPipelinedBufferBytesPerRow
        );
    }

    // ── 2. Output equivalence ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1000)]
    public async Task ColumnPipelinedOutputIsByteIdenticalToRowOriented(int rows)
    {
        List<PipelineWideRow> items = WideRows(rows);
        byte[] rowOriented = await WriteAsync(items, ParquetWriteStrategy.RowOriented);
        byte[] pipelined = await WriteAsync(items, ParquetWriteStrategy.ColumnPipelined);
        Assert.Equal(Hash(rowOriented), Hash(pipelined));
    }

    [Fact]
    public async Task ColumnPipelinedOutputMatchesForArrayAndNonIndexableSources()
    {
        List<PipelineWideRow> items = WideRows(250);
        byte[] fromList = await WriteAsync(items, ParquetWriteStrategy.ColumnPipelined);
        byte[] fromArray = await WriteAsync(items.ToArray(), ParquetWriteStrategy.ColumnPipelined);

        // A HashSet<T> is IReadOnlyCollection<T> but neither List<T> nor T[]; the pipelined path
        // must materialise it once before making N passes.
        var set = new NonIndexableCollection<PipelineWideRow>(items);
        byte[] fromSet = await WriteAsync(set, ParquetWriteStrategy.ColumnPipelined);

        Assert.Equal(Hash(fromList), Hash(fromArray));
        Assert.Equal(Hash(fromList), Hash(fromSet));
        Assert.Equal(
            Hash(fromList),
            Hash(await WriteAsync(items, ParquetWriteStrategy.RowOriented))
        );
    }

    [Fact]
    public async Task ColumnPipelinedRoundTripsValuesAndNulls()
    {
        List<PipelineWideRow> items = WideRows(300);
        byte[] bytes = await WriteAsync(items, ParquetWriteStrategy.ColumnPipelined);

        using var ms = new MemoryStream(bytes);
        List<PipelineWideRow> readBack = await PipelineWideRowParquetExtensions.ReadParquetAsync(
            ms
        );

        Assert.Equal(items.Count, readBack.Count);
        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(items[i].OrderKey, readBack[i].OrderKey);
            Assert.Equal(items[i].ShipDate, readBack[i].ShipDate);
            Assert.Equal(items[i].Quantity, readBack[i].Quantity);
            Assert.Equal(items[i].Comment, readBack[i].Comment);
            Assert.Equal(items[i].ShipInstruct, readBack[i].ShipInstruct);
        }
    }

    [Fact]
    public async Task EmptyChunkWritesNoRowGroupOnEitherStrategy()
    {
        byte[] rowOriented = await WriteAsync([], ParquetWriteStrategy.RowOriented);
        byte[] pipelined = await WriteAsync([], ParquetWriteStrategy.ColumnPipelined);
        Assert.Equal(Hash(rowOriented), Hash(pipelined));
    }

    // ── 3. Strategy selection ──────────────────────────────────────────────

    private static async Task<List<string>> RecordAccessOrderAsync(
        ParquetWriteStrategy strategy,
        long threshold
    )
    {
        var rows = Enumerable.Range(0, 4).Select(i => new PipelineProbeRow(i)).ToList();
        PipelineProbeRow.Accesses.Clear();
        PipelineProbeRow.ThrowOn = null;
        PipelineProbeRow.Recording = true;
        try
        {
            using var ms = new MemoryStream();
            await rows.WriteParquetAsync(
                ms,
                new ParquetSerializerOptions
                {
                    WriteStrategy = strategy,
                    ColumnPipelinedMemoryThresholdBytes = threshold,
                }
            );
        }
        finally
        {
            PipelineProbeRow.Recording = false;
        }
        return [.. PipelineProbeRow.Accesses];
    }

    private static readonly string[] RowOrientedOrder =
    [
        "0.Alpha",
        "0.Beta",
        "0.Gamma",
        "1.Alpha",
        "1.Beta",
        "1.Gamma",
        "2.Alpha",
        "2.Beta",
        "2.Gamma",
        "3.Alpha",
        "3.Beta",
        "3.Gamma",
    ];

    private static readonly string[] PipelinedOrder =
    [
        "0.Alpha",
        "1.Alpha",
        "2.Alpha",
        "3.Alpha",
        "0.Beta",
        "1.Beta",
        "2.Beta",
        "3.Beta",
        "0.Gamma",
        "1.Gamma",
        "2.Gamma",
        "3.Gamma",
    ];

    [Fact]
    public async Task RowOrientedStrategyForcesSinglePassRegardlessOfThreshold()
    {
        Assert.Equal(
            RowOrientedOrder,
            await RecordAccessOrderAsync(ParquetWriteStrategy.RowOriented, threshold: 0)
        );
    }

    [Fact]
    public async Task ColumnPipelinedStrategyForcesColumnPassesRegardlessOfThreshold()
    {
        Assert.Equal(
            PipelinedOrder,
            await RecordAccessOrderAsync(
                ParquetWriteStrategy.ColumnPipelined,
                threshold: long.MaxValue
            )
        );
    }

    [Fact]
    public async Task AutoSelectsColumnPipelinedWhenEstimateExceedsThreshold()
    {
        // 4 rows * 12 bytes/row = 48 > 8.
        Assert.Equal(12, PipelineProbeRowParquetExtensions.PeakRowOrientedBufferBytesPerRow);
        Assert.Equal(
            PipelinedOrder,
            await RecordAccessOrderAsync(ParquetWriteStrategy.Auto, threshold: 8)
        );
    }

    [Fact]
    public async Task AutoStaysRowOrientedWhenEstimateIsAtOrBelowThreshold()
    {
        // Exactly at the threshold must not trip it: the heuristic is a strict '>'.
        Assert.Equal(
            RowOrientedOrder,
            await RecordAccessOrderAsync(ParquetWriteStrategy.Auto, threshold: 48)
        );
        Assert.Equal(
            RowOrientedOrder,
            await RecordAccessOrderAsync(ParquetWriteStrategy.Auto, threshold: 64L * 1024 * 1024)
        );
    }

    [Fact]
    public async Task DefaultOptionsStayRowOriented()
    {
        var rows = Enumerable.Range(0, 3).Select(i => new PipelineProbeRow(i)).ToList();
        PipelineProbeRow.Accesses.Clear();
        PipelineProbeRow.ThrowOn = null;
        PipelineProbeRow.Recording = true;
        try
        {
            using var ms = new MemoryStream();
            await rows.WriteParquetAsync(ms);
        }
        finally
        {
            PipelineProbeRow.Recording = false;
        }
        Assert.Equal("0.Alpha", PipelineProbeRow.Accesses[0]);
        Assert.Equal("0.Beta", PipelineProbeRow.Accesses[1]);
    }

    // ── 4. Pooled buffer lifetime ──────────────────────────────────────────

    /// <summary>
    /// A pool never hands the same array to two rents without an intervening return, so any two
    /// rents coming back as the same instance proves something returned a buffer twice — which is
    /// exactly the failure mode a reuse map can introduce (returning a slot at a column's last use
    /// and again in the finally, or returning an aliased slot under two names).
    /// </summary>
    private static void AssertNoDoubleReturn<T>(int size)
    {
        var rented = new T[6][];
        for (int i = 0; i < rented.Length; i++)
            rented[i] = ArrayPool<T>.Shared.Rent(size);
        try
        {
            for (int i = 0; i < rented.Length; i++)
            {
                for (int j = i + 1; j < rented.Length; j++)
                {
                    Assert.False(
                        ReferenceEquals(rented[i], rented[j]),
                        $"ArrayPool<{typeof(T).Name}> handed out the same array twice: a buffer was returned more than once."
                    );
                }
            }
        }
        finally
        {
            foreach (T[] array in rented)
                ArrayPool<T>.Shared.Return(array);
        }
    }

    private static void AssertPipelineSlotsReturnedExactlyOnce()
    {
        AssertNoDoubleReturn<long>(1024);
        AssertNoDoubleReturn<decimal>(1024);
        AssertNoDoubleReturn<DateTime>(1024);
        AssertNoDoubleReturn<ReadOnlyMemory<char>?>(1024);
        AssertNoDoubleReturn<int>(1024);
    }

    [Fact]
    public async Task SuccessfulPipelinedWriteReturnsEverySlotExactlyOnce()
    {
        await WriteAsync(WideRows(1024), ParquetWriteStrategy.ColumnPipelined);
        AssertPipelineSlotsReturnedExactlyOnce();
    }

    [Fact]
    public async Task ExceptionMidExtractionStillReturnsEverySlotExactlyOnce()
    {
        var rows = Enumerable.Range(0, 512).Select(i => new PipelineProbeRow(i)).ToList();
        PipelineProbeRow.Accesses.Clear();
        PipelineProbeRow.Recording = true;
        PipelineProbeRow.ThrowOn = nameof(PipelineProbeRow.Beta);
        try
        {
            using var ms = new MemoryStream();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await rows.WriteParquetAsync(
                    ms,
                    new ParquetSerializerOptions
                    {
                        WriteStrategy = ParquetWriteStrategy.ColumnPipelined,
                    }
                )
            );
        }
        finally
        {
            PipelineProbeRow.Recording = false;
            PipelineProbeRow.ThrowOn = null;
        }

        // The int slot was live (Alpha written, Beta half-filled) when the throw unwound.
        AssertNoDoubleReturn<int>(1024);

        // And the path is still usable afterwards, producing identical bytes to row-oriented.
        List<PipelineWideRow> good = WideRows(64);
        Assert.Equal(
            Hash(await WriteAsync(good, ParquetWriteStrategy.RowOriented)),
            Hash(await WriteAsync(good, ParquetWriteStrategy.ColumnPipelined))
        );
    }

    [Fact]
    public async Task CancelledPipelinedWriteReturnsEverySlotExactlyOnce()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var ms = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await WideRows(512)
                .WriteParquetAsync(
                    ms,
                    new ParquetSerializerOptions
                    {
                        WriteStrategy = ParquetWriteStrategy.ColumnPipelined,
                    },
                    cts.Token
                )
        );
        AssertPipelineSlotsReturnedExactlyOnce();
    }

    /// <summary>
    /// Pool hygiene for the reused reference-bearing slot. All three string columns share one
    /// pooled <c>ReadOnlyMemory&lt;char&gt;?[]</c>; once the slot has moved on, the values of the
    /// columns behind it must not stay reachable. (Parquet.Net keeps the most recently written
    /// string column's values alive on its own — see UPSTREAM_DEPENDENCY_LIMITATIONS.md — so the
    /// third column is deliberately excluded from the assertion and the row-oriented path is
    /// measured alongside as the control.)
    /// </summary>
    [Theory]
    [InlineData(ParquetWriteStrategy.ColumnPipelined)]
    [InlineData(ParquetWriteStrategy.RowOriented)]
    public async Task ReusedStringSlotDoesNotPinEarlierColumnValues(ParquetWriteStrategy strategy)
    {
        var alive = new List<WeakReference>();

        async Task WriteScopeAsync()
        {
            var rows = new List<PipelineStringRow>(256);
            for (int i = 0; i < 256; i++)
            {
                // Fresh instances rather than interned literals, so reachability is the only
                // thing that can keep them alive.
                string first =
                    new string('f', 24)
                    + i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
                string second =
                    new string('s', 24)
                    + i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
                alive.Add(new WeakReference(first));
                alive.Add(new WeakReference(second));
                rows.Add(
                    new PipelineStringRow
                    {
                        First = first,
                        Second = second,
                        Third =
                            new string('t', 24)
                            + i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
                        Tail = i,
                    }
                );
            }

            using var ms = new MemoryStream();
            await rows.WriteParquetAsync(
                ms,
                new ParquetSerializerOptions { WriteStrategy = strategy }
            );
            rows.Clear();
        }

        await WriteScopeAsync();
        Collect();

        int stillAlive = alive.Count(w => w.IsAlive);
        Assert.True(
            stillAlive == 0,
            $"{stillAlive} of {alive.Count} values from columns behind the reused slot are still "
                + "reachable after the write — a pooled column buffer was returned without being cleared."
        );
    }

    /// <summary>
    /// The pipelined path materialises a source that is neither <c>List&lt;T&gt;</c> nor
    /// <c>T[]</c> into a rented array so it can make N passes over it. That array goes back to the
    /// pool, and it must go back cleared: otherwise every row object of the last chunk stays
    /// reachable from <see cref="ArrayPool{T}"/> for the lifetime of the process. Nothing upstream
    /// ever sees these row objects, so this assertion is entirely about the code added here.
    /// </summary>
    [Fact]
    public async Task PipelinedFallbackDoesNotPinRowObjectsInThePool()
    {
        var alive = new List<WeakReference>();

        async Task WriteScopeAsync()
        {
            List<PipelineWideRow> rows = WideRows(256);
            foreach (PipelineWideRow row in rows)
                alive.Add(new WeakReference(row));

            // Not indexable: forces the rent-and-copy fallback.
            var source = new NonIndexableCollection<PipelineWideRow>(rows);
            using var ms = new MemoryStream();
            await source.WriteParquetAsync(
                ms,
                new ParquetSerializerOptions
                {
                    WriteStrategy = ParquetWriteStrategy.ColumnPipelined,
                }
            );
            rows.Clear();
        }

        await WriteScopeAsync();
        Collect();

        int stillAlive = alive.Count(w => w.IsAlive);
        Assert.True(
            stillAlive == 0,
            $"{stillAlive} of {alive.Count} row objects are still reachable after the write — the "
                + "rented source array was returned to the pool without being cleared."
        );
    }

    private static void Collect()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }
    }

    // ── 5. Unsupported models still honour the option ──────────────────────

    [Fact]
    public async Task NestedModelIgnoresColumnPipelinedRequestAndStillWritesCorrectly()
    {
        var rows = new List<NestedOrder>
        {
            new()
            {
                Id = 1,
                Ship = new Address { City = "Leeds", Zip = 1 },
            },
            new() { Id = 2, Ship = null },
        };

        using var ms = new MemoryStream();
        await rows.WriteParquetAsync(
            ms,
            new ParquetSerializerOptions { WriteStrategy = ParquetWriteStrategy.ColumnPipelined }
        );

        ms.Position = 0;
        List<NestedOrder> readBack = await NestedOrderParquetExtensions.ReadParquetAsync(ms);
        Assert.Equal(2, readBack.Count);
        Assert.Equal("Leeds", readBack[0].Ship?.City);
        Assert.Null(readBack[1].Ship);
    }

    private sealed class NonIndexableCollection<T>(IReadOnlyCollection<T> inner)
        : IReadOnlyCollection<T>
    {
        public int Count => inner.Count;

        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}

/// <summary>
/// The pool-hygiene assertions inspect process-wide <see cref="ArrayPool{T}.Shared"/> state, so
/// this suite must not run beside other tests that rent from it.
/// </summary>
[CollectionDefinition("ColumnPipelinedWrite", DisableParallelization = true)]
#pragma warning disable CA1711
public sealed class ColumnPipelinedWriteCollection { }
#pragma warning restore CA1711
