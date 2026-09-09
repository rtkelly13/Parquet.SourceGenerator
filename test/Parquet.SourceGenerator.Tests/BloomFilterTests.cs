using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Parquet.SourceGenerator.BloomFilters;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record BloomEvent
{
    [ParquetColumn("event_id", BloomFilter = true)]
    public Guid EventId { get; init; }

    [ParquetColumn("session", BloomFilter = true)]
    public string Session { get; init; } = string.Empty;

    [ParquetColumn("sequence", BloomFilter = true)]
    public long Sequence { get; init; }

    [ParquetColumn("payload")]
    public string Payload { get; init; } = string.Empty;
}

/// <summary>
/// Covers the split-block Bloom filter primitives, the footer splice that records them, and the
/// generated point-lookup APIs that probe them (issue #152).
/// </summary>
public sealed class BloomFilterTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BloomFilterTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    private const int RowGroupSize = 250;
    private const int RowCount = 5_000;

    private static List<BloomEvent> BuildRows(int count)
    {
        // Deterministic Guids so a failure is reproducible; the values are still uniformly spread,
        // which is the case Min/Max statistics cannot skip and Bloom filters can.
        var rows = new List<BloomEvent>(count);
        for (int i = 0; i < count; i++)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(i * 2654435761L).CopyTo(bytes, 0);
            BitConverter.GetBytes(unchecked(i * 40503L + 7)).CopyTo(bytes, 8);
            rows.Add(
                new BloomEvent
                {
                    EventId = new Guid(bytes),
                    Session =
                        "session-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Sequence = i,
                    Payload =
                        "payload-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }
            );
        }

        return rows;
    }

    // ── primitives ────────────────────────────────────────────

    [Fact]
    public void XxHash64MatchesReferenceVectors()
    {
        // Cross-checked against System.IO.Hashing.XxHash64.HashToUInt64 (seed 0) over these inputs
        // and 2,000 random buffers. A drift here silently corrupts every filter ever written.
        Assert.Equal(0xEF46DB3751D8E999UL, ParquetXxHash64.Hash(Array.Empty<byte>()));
        Assert.Equal(0xD24EC4F1A98C6E5BUL, ParquetXxHash64.HashUtf8("a"));
        Assert.Equal(0x44BC2CF5AD770999UL, ParquetXxHash64.HashUtf8("abc"));
        Assert.Equal(0x71CE8137CA2DD53DUL, ParquetXxHash64.HashUtf8("abcdefghijklmnop"));
    }

    [Fact]
    public void FilterHasNoFalseNegatives()
    {
        ParquetSplitBlockBloomFilter filter = ParquetSplitBlockBloomFilter.ForExpectedItems(2_000);
        var inserted = new List<ulong>();
        for (int i = 0; i < 2_000; i++)
        {
            ulong hash = ParquetBloomFilterHash.Of("value-" + i);
            inserted.Add(hash);
            filter.Insert(hash);
        }

        Assert.All(inserted, h => Assert.True(filter.MightContain(h)));
    }

    [Fact]
    public void FilterFalsePositiveRateTracksTheRequestedProbability()
    {
        ParquetSplitBlockBloomFilter filter = ParquetSplitBlockBloomFilter.ForExpectedItems(
            10_000,
            0.01
        );
        for (int i = 0; i < 10_000; i++)
        {
            filter.Insert(ParquetBloomFilterHash.Of("member-" + i));
        }

        int falsePositives = 0;
        for (int i = 0; i < 20_000; i++)
        {
            if (filter.MightContain(ParquetBloomFilterHash.Of("absent-" + i)))
            {
                falsePositives++;
            }
        }

        // Sized for 1%; anything above 5% means the sizing or the block selection is wrong rather
        // than merely unlucky.
        Assert.InRange(falsePositives / 20_000.0, 0.0, 0.05);
    }

    [Fact]
    public void BitsetSurvivesASerializationRoundTrip()
    {
        ParquetSplitBlockBloomFilter filter = ParquetSplitBlockBloomFilter.ForExpectedItems(100);
        ulong[] hashes = Enumerable
            .Range(0, 100)
            .Select(i => ParquetBloomFilterHash.Of((long)i))
            .ToArray();
        foreach (ulong h in hashes)
        {
            filter.Insert(h);
        }

        ParquetSplitBlockBloomFilter reloaded = ParquetSplitBlockBloomFilter.FromBytes(
            filter.ToBytes()
        );

        Assert.Equal(filter.ByteCount, reloaded.ByteCount);
        Assert.All(hashes, h => Assert.True(reloaded.MightContain(h)));
    }

    // ── footer splice ─────────────────────────────────────────

    [Fact]
    public async Task WriterRecordsABloomFilterOffsetForEveryAnnotatedColumn()
    {
        byte[] file = await BuildRows(RowCount).WriteParquetWithBloomFiltersAsync(RowGroupSize);

        IReadOnlyList<IReadOnlyList<ParquetBloomFilterLocation>> locations =
            ParquetBloomFilterFooter.ReadLocations(file);

        Assert.Equal(RowCount / RowGroupSize, locations.Count);
        foreach (IReadOnlyList<ParquetBloomFilterLocation> columns in locations)
        {
            Assert.True(GetColumn(columns, "event_id").HasFilter);
            Assert.True(GetColumn(columns, "session").HasFilter);
            Assert.True(GetColumn(columns, "sequence").HasFilter);
            Assert.False(GetColumn(columns, "payload").HasFilter);
        }
    }

    [Fact]
    public async Task SplicedFooterStaysReadableByParquetNet()
    {
        List<BloomEvent> rows = BuildRows(RowCount);
        byte[] file = await rows.WriteParquetWithBloomFiltersAsync(RowGroupSize);

        List<BloomEvent> read = await BloomEventParquetExtensions.ReadParquetAsync(
            new ReadOnlyMemory<byte>(file)
        );

        Assert.Equal(rows, read);
    }

    [Fact]
    public async Task ParquetNetSeesTheSameOffsetsTheSpliceWrote()
    {
        byte[] file = await BuildRows(1_000).WriteParquetWithBloomFiltersAsync(250);

        using var stream = new System.IO.MemoryStream(file, writable: false);
        await using global::Parquet.ParquetReader reader =
            await global::Parquet.ParquetReader.CreateAsync(stream);
        global::Parquet.Meta.FileMetaData metadata = reader.Metadata!;

        IReadOnlyList<IReadOnlyList<ParquetBloomFilterLocation>> ours =
            ParquetBloomFilterFooter.ReadLocations(file);

        for (int r = 0; r < metadata.RowGroups.Count; r++)
        {
            for (int c = 0; c < metadata.RowGroups[r].Columns.Count; c++)
            {
                global::Parquet.Meta.ColumnMetaData meta = metadata
                    .RowGroups[r]
                    .Columns[c]
                    .MetaData!;
                Assert.Equal(ours[r][c].Offset, meta.BloomFilterOffset);
                Assert.Equal(ours[r][c].Length, meta.BloomFilterLength);
            }
        }
    }

    // ── point lookups ─────────────────────────────────────────

    [Fact]
    public async Task GuidPointLookupFindsTheRowAndSkipsEveryOtherRowGroup()
    {
        List<BloomEvent> rows = BuildRows(RowCount);
        byte[] file = await rows.WriteParquetWithBloomFiltersAsync(RowGroupSize);
        BloomEvent target = rows[RowCount - 3];

        var statistics = new ParquetLookupStatistics();
        BloomEvent? found = await BloomEventParquetExtensions.FindByEventIdAsync(
            file,
            target.EventId,
            statistics: statistics
        );

        Assert.Equal(target, found);
        Assert.Equal(RowCount / RowGroupSize, statistics.RowGroupsTotal);
        // The lookup stops at the hit, so every group before the target's must have been
        // eliminated by its Bloom filter rather than read.
        Assert.Equal(statistics.RowGroupsTotal - 1, statistics.RowGroupsSkipped);
        Assert.Equal(1, statistics.RowGroupsScanned);
    }

    [Fact]
    public async Task StringPointLookupFindsTheRow()
    {
        List<BloomEvent> rows = BuildRows(RowCount);
        byte[] file = await rows.WriteParquetWithBloomFiltersAsync(RowGroupSize);

        var statistics = new ParquetLookupStatistics();
        List<BloomEvent> found = await BloomEventParquetExtensions.FindAllBySessionAsync(
            file,
            "session-4321",
            statistics: statistics
        );

        Assert.Single(found);
        Assert.Equal(4321L, found[0].Sequence);
        Assert.Equal(1, statistics.RowGroupsScanned);
        Assert.Equal(statistics.RowGroupsTotal - 1, statistics.RowGroupsSkipped);
    }

    [Fact]
    public async Task Int64PointLookupFindsTheRow()
    {
        List<BloomEvent> rows = BuildRows(RowCount);
        byte[] file = await rows.WriteParquetWithBloomFiltersAsync(RowGroupSize);

        BloomEvent? found = await BloomEventParquetExtensions.FindBySequenceAsync(file, 777L);

        Assert.NotNull(found);
        Assert.Equal("session-777", found!.Session);
    }

    [Fact]
    public async Task AbsentValueEliminatesEveryRowGroup()
    {
        byte[] file = await BuildRows(RowCount).WriteParquetWithBloomFiltersAsync(RowGroupSize);

        var statistics = new ParquetLookupStatistics();
        BloomEvent? found = await BloomEventParquetExtensions.FindByEventIdAsync(
            file,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            statistics: statistics
        );

        Assert.Null(found);
        // At 1% FPP a handful of the twenty groups may still be scanned; the point is that almost
        // none are, and that the ones that are produce no match.
        Assert.Equal(
            statistics.RowGroupsTotal,
            statistics.RowGroupsSkipped + statistics.RowGroupsScanned
        );
        Assert.True(
            statistics.RowGroupsSkipped >= statistics.RowGroupsTotal - 2,
            $"expected almost every row group eliminated, skipped {statistics.RowGroupsSkipped} of {statistics.RowGroupsTotal}"
        );
    }

    [Fact]
    public async Task EveryRowIsStillFindableAfterTheSplice()
    {
        List<BloomEvent> rows = BuildRows(500);
        byte[] file = await rows.WriteParquetWithBloomFiltersAsync(50);

        foreach (BloomEvent row in rows)
        {
            BloomEvent? found = await BloomEventParquetExtensions.FindByEventIdAsync(
                file,
                row.EventId
            );
            Assert.Equal(row, found);
        }
    }

    [Fact]
    public async Task LookupOnAFileWithoutFiltersStillReturnsTheRightRows()
    {
        List<BloomEvent> rows = BuildRows(300);
        using var stream = new System.IO.MemoryStream();
        await rows.WriteParquetBatchedAsync(stream, 50);
        byte[] file = stream.ToArray();

        var statistics = new ParquetLookupStatistics();
        BloomEvent? found = await BloomEventParquetExtensions.FindByEventIdAsync(
            file,
            rows[200].EventId,
            statistics: statistics
        );

        Assert.Equal(rows[200], found);
        Assert.Equal(0, statistics.RowGroupsSkipped);
    }

    [Fact]
    public async Task FilterOverheadIsBoundedAndReported()
    {
        List<BloomEvent> rows = BuildRows(RowCount);

        using var plainStream = new System.IO.MemoryStream();
        await rows.WriteParquetBatchedAsync(plainStream, RowGroupSize);
        byte[] plain = plainStream.ToArray();
        byte[] withFilters = await rows.WriteParquetWithBloomFiltersAsync(RowGroupSize);

        double growth = (withFilters.Length - plain.Length) / (double)plain.Length;
        _output.WriteLine(
            $"plain={plain.Length} bytes, withFilters={withFilters.Length} bytes, growth={growth:P2}"
        );

        // Three filters per row group at 1% FPP over 250 rows each. The filters are a real cost
        // paid on every write, and the assertion pins it so a sizing regression is visible.
        Assert.True(withFilters.Length > plain.Length);
        Assert.InRange(growth, 0.0, 1.5);

        // Same model at a realistic row group size: the per-filter minimum amortises over far more
        // rows, so the same three filters cost proportionally much less.
        List<BloomEvent> wide = BuildRows(40_000);
        using var widePlainStream = new System.IO.MemoryStream();
        await wide.WriteParquetBatchedAsync(widePlainStream, 10_000);
        byte[] widePlain = widePlainStream.ToArray();
        byte[] wideFiltered = await wide.WriteParquetWithBloomFiltersAsync(10_000);
        double wideGrowth = (wideFiltered.Length - widePlain.Length) / (double)widePlain.Length;
        _output.WriteLine(
            $"rowGroup=10000 plain={widePlain.Length} bytes, withFilters={wideFiltered.Length} bytes, growth={wideGrowth:P2}"
        );

        Assert.InRange(wideGrowth, 0.0, growth);
    }

    private static ParquetBloomFilterLocation GetColumn(
        IReadOnlyList<ParquetBloomFilterLocation> columns,
        string path
    ) => columns.First(c => string.Equals(c.ColumnPath, path, StringComparison.Ordinal));
}
