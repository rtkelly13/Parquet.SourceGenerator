using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record PrunedOrder
{
    [ParquetColumn("order_key")]
    public long OrderKey { get; init; }

    [ParquetColumn("region")]
    public string Region { get; init; } = string.Empty;

    [ParquetColumn("quantity")]
    public int Quantity { get; init; }
}

/// <summary>
/// Row-group predicate pushdown (issue #149): the generated readers take an optional predicate over
/// footer statistics and never open the column pages of a row group the zone map rules out.
/// </summary>
/// <remarks>
/// Two things are worth pinning down, and they are different claims. Correctness — the surviving
/// rows are exactly the rows of the surviving row groups, and pruning never drops a row a predicate
/// admits. And the actual saving — a counting stream proves the skipped groups cost no bytes read,
/// which is the whole point of the feature and the part a plain LINQ <c>Where</c> cannot do.
/// </remarks>
public sealed class RowGroupPruningTests
{
    private const int RowsPerGroup = 500;
    private const int GroupCount = 10;

    /// <summary>
    /// Ten row groups of 500 rows, OrderKey running 0..4999 so group <c>g</c> owns exactly
    /// <c>[g*500, g*500+499]</c> — a clean zone map per group.
    /// </summary>
    private static async Task<byte[]> WriteAsync()
    {
        List<PrunedOrder> rows = Enumerable
            .Range(0, RowsPerGroup * GroupCount)
            .Select(i => new PrunedOrder
            {
                OrderKey = i,
                Region = $"region_{i / RowsPerGroup}",
                Quantity = i % 7,
            })
            .ToList();

        using var stream = new MemoryStream();
        await rows.WriteParquetBatchedAsync(stream, RowsPerGroup);
        return stream.ToArray();
    }

    /// <summary>Counts the bytes a reader actually pulls, so a skipped row group is visible as I/O never done.</summary>
    private sealed class CountingStream : Stream
    {
        private readonly MemoryStream _inner;

        public CountingStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);

        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _inner.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            int read = _inner.Read(buffer.Span);
            BytesRead += read;
            return new ValueTask<int>(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task InequalityPredicateKeepsOnlyTheRowGroupsWhoseMaxReachesTheThreshold()
    {
        byte[] bytes = await WriteAsync();

        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta => meta.OrderKey.MayContainAtLeast(4_000)
        );

        // Groups 8 and 9 hold keys 4000..4999; every earlier group's max is below the threshold.
        Assert.Equal(2 * RowsPerGroup, pruned.Count);
        Assert.Equal(4_000, pruned[0].OrderKey);
        Assert.Equal(4_999, pruned[^1].OrderKey);
    }

    [Fact]
    public async Task RawMinMaxComparisonsReadTheSameWayTheIssueSpellsThem()
    {
        byte[] bytes = await WriteAsync();

        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta => meta.OrderKey.Max >= 1_000
        );

        // Group 1's max is 999, so the first group the raw comparison admits is group 2.
        Assert.Equal(8 * RowsPerGroup, pruned.Count);
        Assert.Equal(1_000, pruned[0].OrderKey);
    }

    [Fact]
    public async Task UpperBoundPredicatePrunesGroupsWhoseMinIsAlreadyPastIt()
    {
        byte[] bytes = await WriteAsync();

        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta => meta.OrderKey.MayContainAtMost(999)
        );

        Assert.Equal(2 * RowsPerGroup, pruned.Count);
        Assert.Equal(0, pruned[0].OrderKey);
        Assert.Equal(999, pruned[^1].OrderKey);
    }

    [Fact]
    public async Task EqualityPredicateKeepsTheSingleGroupWhoseRangeStraddlesTheValue()
    {
        byte[] bytes = await WriteAsync();

        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta => meta.OrderKey.MayContain(2_222)
        );

        Assert.Equal(RowsPerGroup, pruned.Count);
        Assert.Contains(pruned, o => o.OrderKey == 2_222);
        Assert.All(pruned, o => Assert.InRange(o.OrderKey, 2_000, 2_499));
    }

    [Fact]
    public async Task ConjunctiveMultiColumnPredicateFailsTheGroupOnTheFirstColumnThatCannotMatch()
    {
        byte[] bytes = await WriteAsync();

        // The key range admits groups 4..9; the region equality admits only group 4.
        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta =>
                meta.OrderKey.MayContainAtLeast(2_000) && meta.Region.MayContain("region_4")
        );

        Assert.Equal(RowsPerGroup, pruned.Count);
        Assert.All(pruned, o => Assert.Equal("region_4", o.Region));
    }

    [Fact]
    public async Task APredicateNoGroupCanSatisfyReturnsNothing()
    {
        byte[] bytes = await WriteAsync();

        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta => meta.OrderKey.MayContainAtLeast(1_000_000)
        );

        Assert.Empty(pruned);
    }

    [Fact]
    public async Task PruningNeverDropsARowTheEquivalentLinqFilterKeeps()
    {
        byte[] bytes = await WriteAsync();

        List<PrunedOrder> all = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes)
        );
        List<PrunedOrder> expected = all.Where(o => o.OrderKey >= 3_100).ToList();

        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta => meta.OrderKey.MayContainAtLeast(3_100)
        );

        // Pruning is a superset filter: whole groups only, so the surviving set still contains
        // every row the row-level predicate would keep.
        Assert.Equal(RowsPerGroup * 4, pruned.Count);
        Assert.Empty(expected.Select(o => o.OrderKey).Except(pruned.Select(o => o.OrderKey)));
        Assert.All(pruned.Where(o => o.OrderKey >= 3_100), o => Assert.Contains(o, expected));
    }

    [Fact]
    public async Task SkippedRowGroupsCostNoBytesRead()
    {
        byte[] bytes = await WriteAsync();

        using var fullStream = new CountingStream(bytes);
        List<PrunedOrder> all = await PrunedOrderParquetExtensions.ReadParquetAsync(fullStream);

        using var prunedStream = new CountingStream(bytes);
        List<PrunedOrder> pruned = await PrunedOrderParquetExtensions.ReadParquetAsync(
            prunedStream,
            predicate: meta => meta.OrderKey.MayContainAtLeast(4_500)
        );

        Assert.Equal(RowsPerGroup * GroupCount, all.Count);
        Assert.Equal(RowsPerGroup, pruned.Count);

        // One group of ten survives, so the data pages read should collapse by roughly an order of
        // magnitude. The footer is read either way, so this is deliberately a loose bound.
        Assert.True(
            prunedStream.BytesRead * 4 < fullStream.BytesRead,
            $"pruned read {prunedStream.BytesRead} bytes, full read {fullStream.BytesRead}"
        );
    }

    [Fact]
    public async Task ArrayOverloadPrunesTheSameWay()
    {
        byte[] bytes = await WriteAsync();

        PrunedOrder[] pruned = await PrunedOrderParquetExtensions.ReadParquetArrayAsync(
            new MemoryStream(bytes),
            predicate: meta => meta.OrderKey.MayContainAtLeast(4_500)
        );

        Assert.Equal(RowsPerGroup, pruned.Length);
        Assert.Equal(4_500, pruned[0].OrderKey);
    }

    [Fact]
    public async Task StreamingOverloadPrunesTheSameWay()
    {
        byte[] bytes = await WriteAsync();

        var streamed = new List<PrunedOrder>();
        await foreach (
            PrunedOrder item in PrunedOrderParquetExtensions.ReadParquetStreamAsync(
                new MemoryStream(bytes),
                predicate: meta => meta.OrderKey.MayContainAtLeast(4_500)
            )
        )
        {
            streamed.Add(item);
        }

        Assert.Equal(RowsPerGroup, streamed.Count);
        Assert.Equal(4_500, streamed[0].OrderKey);
    }

    [Fact]
    public async Task BufferStreamingOverloadPrunesTheSameWay()
    {
        byte[] bytes = await WriteAsync();

        var streamed = new List<PrunedOrder>();
        await foreach (
            PrunedOrder item in PrunedOrderParquetExtensions.ReadParquetStreamAsync(
                new ReadOnlyMemory<byte>(bytes),
                predicate: meta => meta.OrderKey.MayContainAtLeast(4_500)
            )
        )
        {
            streamed.Add(item);
        }

        Assert.Equal(RowsPerGroup, streamed.Count);
    }

    [Fact]
    public async Task NoPredicateReadsEveryRowExactlyAsBefore()
    {
        byte[] bytes = await WriteAsync();

        List<PrunedOrder> all = await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes)
        );

        Assert.Equal(RowsPerGroup * GroupCount, all.Count);
        Assert.Equal(
            Enumerable.Range(0, RowsPerGroup * GroupCount).Select(i => (long)i),
            all.Select(o => o.OrderKey)
        );
    }

    [Fact]
    public async Task MetadataCarriesRowGroupIdentityAndNullCounts()
    {
        byte[] bytes = await WriteAsync();

        var seen = new List<(int Index, long RowCount, long? Min, long? Max, long? Nulls)>();
        await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta =>
            {
                seen.Add(
                    (
                        meta.RowGroupIndex,
                        meta.RowCount,
                        meta.OrderKey.Min,
                        meta.OrderKey.Max,
                        meta.OrderKey.NullCount
                    )
                );
                return false;
            }
        );

        Assert.Equal(GroupCount, seen.Count);
        Assert.Equal(Enumerable.Range(0, GroupCount), seen.Select(s => s.Index));
        Assert.All(seen, s => Assert.Equal(RowsPerGroup, s.RowCount));
        Assert.All(seen, s => Assert.Equal(0, s.Nulls));
        Assert.Equal(0, seen[0].Min);
        Assert.Equal(499, seen[0].Max);
        Assert.Equal(4_500, seen[^1].Min);
        Assert.Equal(4_999, seen[^1].Max);
        Assert.All(seen, s => Assert.True(s.Max - s.Min == RowsPerGroup - 1));
    }

    [Fact]
    public async Task ThePredicateRunsOncePerRowGroupNotOncePerRow()
    {
        byte[] bytes = await WriteAsync();

        int calls = 0;
        await PrunedOrderParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            predicate: meta =>
            {
                calls++;
                return meta.RowGroupIndex == 0;
            }
        );

        Assert.Equal(GroupCount, calls);
    }
}

/// <summary>
/// The projection layer under the generated predicate: what a zone map answers when statistics are
/// present, absent, or recorded in a physical type that cannot be mapped back losslessly.
/// </summary>
public sealed class ParquetColumnStatisticsTests
{
    [Fact]
    public void RangeHelpersAnswerTheInclusionQuestionsAPredicateAsks()
    {
        ParquetColumnStatistics<int> stats = ParquetColumnStatistics.FromRaw<int>(10, 20, 0, null);

        Assert.True(stats.HasMinMax);
        Assert.Equal(10, stats.Min);
        Assert.Equal(20, stats.Max);
        Assert.True(stats.IsKnownNonNull);

        Assert.True(stats.MayContain(15));
        Assert.False(stats.MayContain(9));
        Assert.False(stats.MayContain(21));

        Assert.True(stats.MayContainAtLeast(20));
        Assert.False(stats.MayContainAtLeast(21));
        Assert.False(stats.MayContainGreaterThan(20));

        Assert.True(stats.MayContainAtMost(10));
        Assert.False(stats.MayContainAtMost(9));
        Assert.False(stats.MayContainLessThan(10));

        Assert.True(stats.MayContainBetween(5, 12));
        Assert.False(stats.MayContainBetween(0, 9));
        Assert.False(stats.MayContainBetween(21, 30));

        Assert.True(stats.MayContainAny(1, 15));
        Assert.False(stats.MayContainAny(1, 2));
        Assert.False(stats.MayContainAny());
    }

    [Fact]
    public void MissingStatisticsMakeEveryHelperAnswerYes()
    {
        ParquetColumnStatistics<int> stats = ParquetColumnStatistics.FromRaw<int>(
            null,
            null,
            null,
            null
        );

        Assert.False(stats.HasMinMax);
        Assert.True(stats.MayContain(15));
        Assert.True(stats.MayContainAtLeast(int.MaxValue));
        Assert.True(stats.MayContainAtMost(int.MinValue));
        Assert.True(stats.MayContainBetween(1, 2));
        Assert.True(stats.MayContainAny(7));
    }

    [Fact]
    public void PhysicalInt32StatisticsProjectOntoNarrowerClrTypes()
    {
        // Parquet stores a short column as INT32; the projection has to widen back down.
        ParquetColumnStatistics<short> stats = ParquetColumnStatistics.FromRaw<short>(
            -5,
            1_000,
            0,
            null
        );

        Assert.True(stats.HasMinMax);
        Assert.Equal((short)-5, stats.Min);
        Assert.Equal((short)1_000, stats.Max);
    }

    [Fact]
    public void AValueThatCannotBeProjectedDisablesPruningRatherThanGuessing()
    {
        // An INT32 statistic that overflows the property's own type must not silently truncate.
        ParquetColumnStatistics<short> overflowed = ParquetColumnStatistics.FromRaw<short>(
            0,
            int.MaxValue,
            0,
            null
        );

        Assert.False(overflowed.HasMinMax);
        Assert.True(overflowed.MayContainAtLeast(short.MaxValue));

        ParquetColumnStatistics<int> nonNumeric = ParquetColumnStatistics.FromRaw<int>(
            new object(),
            new object(),
            null,
            null
        );

        Assert.False(nonNumeric.HasMinMax);
        Assert.True(nonNumeric.MayContain(0));
    }

    [Fact]
    public void StringStatisticsCompareOrdinally()
    {
        ParquetColumnStatistics<string> stats = ParquetColumnStatistics.FromRaw<string>(
            "alpha",
            "omega",
            0,
            null
        );

        Assert.True(stats.MayContain("delta"));
        Assert.False(stats.MayContain("zulu"));
        Assert.False(stats.MayContain("Alpha"));
    }

    [Fact]
    public void ZoneMapsCompareByValue()
    {
        ParquetColumnStatistics<int> a = ParquetColumnStatistics.FromRaw<int>(1, 2, 0, null);
        ParquetColumnStatistics<int> b = ParquetColumnStatistics.FromRaw<int>(1, 2, 0, null);
        ParquetColumnStatistics<int> c = ParquetColumnStatistics.FromRaw<int>(1, 3, 0, null);

        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != c);
    }
}
