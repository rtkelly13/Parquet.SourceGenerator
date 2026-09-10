using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// A time-series shaped row: monotonically increasing <c>SequenceNumber</c> and
/// <c>Timestamp</c>, an unsorted <c>Bucket</c>, and a payload column so a row group is
/// worth skipping.
/// <para>
/// Three columns carry <c>[ParquetSortKey]</c> and so get lookup overloads; <c>Payload</c>
/// does not, and is the control for
/// <see cref="SortedRowGroupPruningTests.UnmarkedColumnGetsNoLookupOverloads"/>. <c>Bucket</c>
/// is marked but is not actually written in sorted order — the marker asks for the lookup, it
/// does not promise the data is sorted, and the read has to notice that at runtime.
/// </para>
/// </summary>
[ParquetSerializable]
public partial record SortedEvent
{
    [ParquetColumn("sequence_number")]
    [ParquetSortKey]
    public long SequenceNumber { get; init; }

    [ParquetColumn("timestamp")]
    [ParquetSortKey]
    public DateTime Timestamp { get; init; }

    [ParquetColumn("bucket")]
    [ParquetSortKey]
    public int Bucket { get; init; }

    [ParquetColumn("payload")]
    public string Payload { get; init; } = string.Empty;
}

/// <summary>
/// The same shape with no <c>[ParquetSortKey]</c> anywhere: the opt-out case, whose generated
/// class must carry none of the pruning API.
/// </summary>
[ParquetSerializable]
public partial record UnmarkedEvent
{
    [ParquetColumn("sequence_number")]
    public long SequenceNumber { get; init; }

    [ParquetColumn("payload")]
    public string Payload { get; init; } = string.Empty;
}

/// <summary>
/// Issue #151: point lookups and range slices prune row groups using the footer
/// <c>[Min, Max]</c> statistics.
/// <para>
/// The properties worth pinning are correctness first — a pruned read must return exactly
/// what a full scan returns, including on unsorted data where pruning has to switch itself
/// off — and only then the observable fact that row groups were skipped. The skip count is
/// asserted through <see cref="ParquetPruneStatistics"/> rather than wall-clock time, which
/// would make the suite flaky on a loaded runner.
/// </para>
/// </summary>
public sealed class SortedRowGroupPruningTests
{
    private static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<SortedEvent> SortedRows(int count) =>
        Enumerable
            .Range(0, count)
            .Select(i => new SortedEvent
            {
                SequenceNumber = i,
                Timestamp = Epoch.AddSeconds(i),
                Bucket = i % 7,
                Payload = $"payload-{i}",
            })
            .ToList();

    private static async Task<byte[]> WriteAsync(List<SortedEvent> rows, int rowGroupSize)
    {
        using var stream = new MemoryStream();
        await rows.WriteParquetBatchedAsync(stream, rowGroupSize);
        return stream.ToArray();
    }

    private static MemoryStream Open(byte[] bytes) => new(bytes, writable: false);

    [Fact]
    public async Task PointLookupOnSortedKeyReadsExactlyOneRowGroup()
    {
        byte[] bytes = await WriteAsync(SortedRows(10_000), rowGroupSize: 100);
        var pruning = new ParquetPruneStatistics();

        List<SortedEvent> found =
            await SortedEventParquetExtensions.ReadParquetBySequenceNumberAsync(
                Open(bytes),
                7_531,
                pruning
            );

        Assert.Single(found);
        Assert.Equal(7_531, found[0].SequenceNumber);
        Assert.True(pruning.SortedColumnDetected);
        Assert.True(pruning.StrictlyMonotonic);
        Assert.Equal(100, pruning.RowGroupCount);
        Assert.Equal(1, pruning.RowGroupsScanned);
        Assert.Equal(99, pruning.RowGroupsPruned);
        Assert.Equal(75, pruning.FirstRowGroupRead);
    }

    [Fact]
    public async Task PointLookupOnAMissingKeyReadsNothingAndReturnsEmpty()
    {
        byte[] bytes = await WriteAsync(SortedRows(1_000), rowGroupSize: 100);
        var pruning = new ParquetPruneStatistics();

        List<SortedEvent> found =
            await SortedEventParquetExtensions.ReadParquetBySequenceNumberAsync(
                Open(bytes),
                50_000,
                pruning
            );

        Assert.Empty(found);
        Assert.True(pruning.SortedColumnDetected);
        Assert.Equal(0, pruning.RowGroupsScanned);
        Assert.Equal(-1, pruning.FirstRowGroupRead);
    }

    [Fact]
    public async Task RangeSliceReadsOnlyTheOverlappingRowGroups()
    {
        byte[] bytes = await WriteAsync(SortedRows(10_000), rowGroupSize: 100);
        var pruning = new ParquetPruneStatistics();

        List<SortedEvent> slice =
            await SortedEventParquetExtensions.ReadParquetSequenceNumberRangeAsync(
                Open(bytes),
                2_050,
                2_349,
                pruning
            );

        Assert.Equal(300, slice.Count);
        Assert.Equal(2_050, slice[0].SequenceNumber);
        Assert.Equal(2_349, slice[^1].SequenceNumber);
        Assert.True(pruning.SortedColumnDetected);
        // Groups 20..23 overlap [2050, 2349]; the other 96 never open.
        Assert.Equal(4, pruning.RowGroupsScanned);
        Assert.Equal(20, pruning.FirstRowGroupRead);
        Assert.Equal(23, pruning.LastRowGroupRead);
    }

    [Fact]
    public async Task RangeSliceMatchesAFullScanFilterExactly()
    {
        List<SortedEvent> rows = SortedRows(5_000);
        byte[] bytes = await WriteAsync(rows, rowGroupSize: 250);

        List<SortedEvent> expected = rows.Where(r =>
                r.SequenceNumber >= 1_234 && r.SequenceNumber <= 3_210
            )
            .ToList();

        List<SortedEvent> actual =
            await SortedEventParquetExtensions.ReadParquetSequenceNumberRangeAsync(
                Open(bytes),
                1_234,
                3_210
            );

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Select(r => r.SequenceNumber), actual.Select(r => r.SequenceNumber));
        Assert.Equal(expected.Select(r => r.Payload), actual.Select(r => r.Payload));
    }

    [Fact]
    public async Task DateTimeKeyPrunesTheSameWayAsAnIntegerKey()
    {
        byte[] bytes = await WriteAsync(SortedRows(2_000), rowGroupSize: 100);
        var pruning = new ParquetPruneStatistics();

        List<SortedEvent> slice = await SortedEventParquetExtensions.ReadParquetTimestampRangeAsync(
            Open(bytes),
            Epoch.AddSeconds(1_500),
            Epoch.AddSeconds(1_599),
            pruning
        );

        Assert.Equal(100, slice.Count);
        Assert.True(pruning.SortedColumnDetected);
        Assert.Equal(1, pruning.RowGroupsScanned);
    }

    [Fact]
    public async Task UnsortedKeyFallsBackToAFullScanAndStillAnswersCorrectly()
    {
        // Bucket cycles 0..6 inside every row group, so every row group's [Min, Max] is [0, 6]:
        // maximally overlapping, and nothing can be pruned.
        List<SortedEvent> rows = SortedRows(1_000);
        byte[] bytes = await WriteAsync(rows, rowGroupSize: 100);
        var pruning = new ParquetPruneStatistics();

        List<SortedEvent> found = await SortedEventParquetExtensions.ReadParquetByBucketAsync(
            Open(bytes),
            3,
            pruning
        );

        Assert.False(pruning.SortedColumnDetected);
        Assert.Equal(10, pruning.RowGroupCount);
        Assert.Equal(10, pruning.RowGroupsScanned);
        Assert.Equal(rows.Count(r => r.Bucket == 3), found.Count);
        Assert.All(found, r => Assert.Equal(3, r.Bucket));
    }

    [Fact]
    public async Task DuplicateKeysStraddlingARowGroupBoundaryAreAllReturned()
    {
        // Two row groups of 100 rows each carry the same key 42, so [Min, Max] touch at the
        // boundary: sorted but not strictly monotonic, and both groups must be read.
        var rows = new List<SortedEvent>();
        for (int i = 0; i < 200; i++)
        {
            rows.Add(
                new SortedEvent
                {
                    SequenceNumber = 42,
                    Timestamp = Epoch,
                    Bucket = 0,
                    Payload = $"dup-{i}",
                }
            );
        }
        for (int i = 0; i < 100; i++)
        {
            rows.Add(
                new SortedEvent
                {
                    SequenceNumber = 100 + i,
                    Timestamp = Epoch.AddSeconds(1 + i),
                    Bucket = 0,
                    Payload = $"tail-{i}",
                }
            );
        }

        byte[] bytes = await WriteAsync(rows, rowGroupSize: 100);
        var pruning = new ParquetPruneStatistics();

        List<SortedEvent> found =
            await SortedEventParquetExtensions.ReadParquetBySequenceNumberAsync(
                Open(bytes),
                42,
                pruning
            );

        Assert.Equal(200, found.Count);
        Assert.True(pruning.SortedColumnDetected);
        Assert.False(pruning.StrictlyMonotonic);
        Assert.Equal(2, pruning.RowGroupsScanned);
    }

    [Fact]
    public async Task SingleRowGroupFileIsHandledWithoutPruningAnything()
    {
        byte[] bytes = await WriteAsync(SortedRows(50), rowGroupSize: 1_000);
        var pruning = new ParquetPruneStatistics();

        List<SortedEvent> found =
            await SortedEventParquetExtensions.ReadParquetBySequenceNumberAsync(
                Open(bytes),
                17,
                pruning
            );

        Assert.Single(found);
        Assert.Equal(1, pruning.RowGroupCount);
        Assert.Equal(1, pruning.RowGroupsScanned);
    }

    [Fact]
    public async Task InvertedRangeReturnsEmptyWithoutOpeningTheFile()
    {
        byte[] bytes = await WriteAsync(SortedRows(500), rowGroupSize: 100);

        List<SortedEvent> found =
            await SortedEventParquetExtensions.ReadParquetSequenceNumberRangeAsync(
                Open(bytes),
                400,
                100
            );

        Assert.Empty(found);
    }

    [Fact]
    public async Task NullStreamThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            SortedEventParquetExtensions.ReadParquetBySequenceNumberAsync((Stream)null!, 1)
        );
    }

    /// <summary>
    /// The opt-in guarantee, asserted against the real generator output rather than the
    /// emitter: a model with no <c>[ParquetSortKey]</c> gains no public API at all — not the
    /// lookups, not the range overloads, and not the shared pruning core they forward into.
    /// </summary>
    [Fact]
    public void UnmarkedModelGetsNoneOfTheLookupApi()
    {
        string[] emitted = typeof(UnmarkedEventParquetExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain("ReadParquetBySequenceNumberAsync", emitted);
        Assert.DoesNotContain("ReadParquetSequenceNumberRangeAsync", emitted);
        Assert.DoesNotContain("ReadPrunedRangeAsync", emitted);
        Assert.DoesNotContain("TryPruneSortedRowGroups", emitted);
        Assert.DoesNotContain("TryCompareStatistics", emitted);
        Assert.DoesNotContain("TryCompareStatisticToKey", emitted);

        // The ordinary read API is untouched, so the absence above is opt-in and not a
        // generator that simply failed to run for this model.
        Assert.Contains("ReadParquetAsync", emitted);
    }

    /// <summary>
    /// The per-column half of the same guarantee: <c>Payload</c> shares a model with three
    /// marked columns, and still gets nothing.
    /// </summary>
    [Fact]
    public void UnmarkedColumnGetsNoLookupOverloads()
    {
        string[] emitted = typeof(SortedEventParquetExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain("ReadParquetByPayloadAsync", emitted);
        Assert.DoesNotContain("ReadParquetPayloadRangeAsync", emitted);
        Assert.Contains("ReadParquetBySequenceNumberAsync", emitted);
    }
}
