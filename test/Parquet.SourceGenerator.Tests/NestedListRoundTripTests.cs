using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parquet.SourceGenerator;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// M3a of issue #176: row-level lists/arrays with leaf elements. Null-vs-empty list
/// distinction, null elements, element conversions (string, Guid, byte[], DateTime?),
/// and the repetition-level entry walk — per docs/15 §1.
/// </summary>
public sealed class NestedListRoundTripTests
{
    [Fact]
    public async Task ListMembersRoundTripWithNullAndEmptyDistinction()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var d1 = new DateTime(2026, 5, 4, 3, 2, 1);
        var expectedTags0 = new string?[] { "a", null, "b" };
        var expectedScores0 = new[] { 1, 2, 3 };
        var expectedWhen0 = new DateTime?[] { d1, null };
        var expectedBlob0 = new byte[] { 1, 2 };
        var expectedBlobList = new[] { expectedBlob0 };
        var rows = new List<ListRow>
        {
            new()
            {
                Id = 0,
                Tags = new List<string?> { "a", null, "b" },
                Scores = new List<int> { 1, 2, 3 },
                Keys = new[] { g1, g2 },
                When = new List<DateTime?> { d1, null },
                Blobs = new List<byte[]> { new byte[] { 1, 2 } },
            },
            new()
            {
                Id = 1,
                Tags = new List<string?>(),
                Scores = new List<int>(),
                Keys = Array.Empty<Guid>(),
                When = new List<DateTime?>(),
                Blobs = new List<byte[]>(),
            },
            new()
            {
                Id = 2,
                Tags = null,
                Scores = null!,
                Keys = null,
                When = null,
                Blobs = null,
            },
            new()
            {
                Id = 3,
                Tags = new List<string?> { null },
                Scores = new List<int> { 7 },
                Keys = null,
                When = new List<DateTime?> { null },
                Blobs = null,
            },
        };

        var ms = new MemoryStream();
        await ListRowParquetExtensions.WriteParquetAsync(rows, ms);
        ms.Position = 0;
        var back = await ListRowParquetExtensions.ReadParquetAsync(ms);

        Assert.Equal(4, back.Count);
        Assert.Equal(expectedTags0, back[0].Tags!.ToArray());
        Assert.Equal(expectedScores0, back[0].Scores!.ToArray());
        Assert.Equal(new[] { g1, g2 }, back[0].Keys!);
        Assert.Equal(expectedWhen0, back[0].When!.ToArray());
        Assert.Equal(expectedBlobList, back[0].Blobs!.Select(b => b.ToArray()).ToArray());

        Assert.Empty(back[1].Tags!);
        Assert.Empty(back[1].Scores!);
        Assert.Empty(back[1].Keys!);
        Assert.Empty(back[1].When!);
        Assert.Empty(back[1].Blobs!);

        Assert.Null(back[2].Tags);
        Assert.Null(back[2].Keys);
        Assert.Null(back[2].When);
        Assert.Null(back[2].Blobs);

        var expectedTags3 = new string?[] { null };
        var expectedScores3 = new[] { 7 };
        var expectedWhen3 = new DateTime?[] { null };
        Assert.Equal(expectedTags3, back[3].Tags!.ToArray());
        Assert.Equal(expectedScores3, back[3].Scores!.ToArray());
        Assert.Null(back[3].Keys);
        Assert.Equal(expectedWhen3, back[3].When!.ToArray());
    }

    [Fact]
    public async Task MultiRowGroupsAndArrayPathsKeepListEntries()
    {
        var rows = Enumerable
            .Range(0, 50)
            .Select(i => new ListRow
            {
                Id = i,
                Tags = i % 3 == 0 ? null : new List<string?> { $"t{i}", null },
                Scores = Enumerable.Range(0, i % 5).ToList(),
                Keys = new[] { Guid.NewGuid() },
                When = new List<DateTime?> { new DateTime(2020 + (i % 7), 1, 1) },
                Blobs = new List<byte[]> { new byte[] { (byte)i } },
            })
            .ToList();

        var ms = new MemoryStream();
        await rows.WriteParquetBatchedAsync(ms, new ParquetSerializerOptions { RowGroupSize = 7 });
        ms.Position = 0;
        var back = await ListRowParquetExtensions.ReadParquetParallelArrayAsync(ms);

        Assert.Equal(50, back.Length);
        for (int i = 0; i < 50; i++)
        {
            if (i % 3 == 0)
                Assert.Null(back[i].Tags);
            else
            {
                var expectedTagsI = new string?[] { $"t{i}", null };
                Assert.Equal(expectedTagsI, back[i].Tags!.ToArray());
            }
            Assert.Equal(Enumerable.Range(0, i % 5).ToArray(), back[i].Scores!.ToArray());
        }
    }
}

[ParquetSerializable]
public sealed partial record ListRow
{
    public int Id { get; init; }
    public List<string?>? Tags { get; init; }
    public List<int>? Scores { get; init; }
    public Guid[]? Keys { get; init; }
    public List<DateTime?>? When { get; init; }
    public List<byte[]>? Blobs { get; init; }

    [Fact]
    public async Task ListOfPocoRoundTripPreservesMarkersFieldsAndAlignment()
    {
        var node1 = Guid.NewGuid();
        var node2 = Guid.NewGuid();
        var rows = new List<TripRow>
        {
            new()
            {
                Id = 0,
                Stops = null,
                Route = null,
            },
            new()
            {
                Id = 1,
                Stops = [],
                Route = [],
            },
            new()
            {
                Id = 2,
                Stops =
                [
                    new Stop
                    {
                        City = "A",
                        Zip = 1,
                        Node = node1,
                    },
                    new Stop
                    {
                        City = null,
                        Zip = null,
                        Node = node2,
                    },
                ],
                Route =
                [
                    new Stop
                    {
                        City = "R",
                        Zip = 9,
                        Node = node1,
                    },
                ],
            },
            new()
            {
                Id = 3,
                Stops =
                [
                    new Stop
                    {
                        City = "Z",
                        Zip = 0,
                        Node = Guid.NewGuid(),
                    },
                ],
            },
        };

        var ms = new MemoryStream();
        await TripRowParquetExtensions.WriteParquetAsync(rows, ms);
        ms.Position = 0;

        var back = await TripRowParquetExtensions.ReadParquetParallelArrayAsync(ms);

        Assert.Equal(4, back.Length);
        Assert.Null(back[0].Stops);
        Assert.Null(back[0].Route);
        Assert.Empty(back[1].Stops!);
        Assert.Empty(back[1].Route!);
        Assert.Equal(2, back[2].Stops!.Count);
        Assert.Equal("A", back[2].Stops[0].City);
        Assert.Equal(1, back[2].Stops[0].Zip);
        Assert.Equal(node1, back[2].Stops[0].Node);
        Assert.Null(back[2].Stops[1].City);
        Assert.Null(back[2].Stops[1].Zip);
        Assert.Equal(node2, back[2].Stops[1].Node);
        Assert.Single(back[2].Route!);
        Assert.Equal("R", back[2].Route![0].City);
        Assert.Equal(9, back[2].Route![0].Zip!.Value);
        Assert.Single(back[3].Stops!);
        Assert.Equal(0, back[3].Stops![0].Zip!.Value);
    }

    [Fact]
    public async Task MultiRowGroupListOfPocoReadsBackAligned()
    {
        var rows = new List<TripRow>();
        for (int i = 0; i < 50; i++)
        {
            rows.Add(
                new TripRow
                {
                    Id = i,
                    Stops =
                    [
                        new Stop
                        {
                            City = $"c{i}",
                            Zip = i % 2,
                            Node = Guid.NewGuid(),
                        },
                        new Stop
                        {
                            City = null,
                            Zip = null,
                            Node = Guid.NewGuid(),
                        },
                    ],
                }
            );
        }

        var ms = new MemoryStream();
        await rows.WriteParquetBatchedAsync(ms, new ParquetSerializerOptions { RowGroupSize = 10 });
        ms.Position = 0;

        var back = await TripRowParquetExtensions.ReadParquetParallelArrayAsync(ms);

        Assert.Equal(50, back.Length);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(2, back[i].Stops!.Count);
            Assert.Equal($"c{i}", back[i].Stops[0].City);
            Assert.Equal(i % 2, back[i].Stops[0].Zip);
            Assert.Null(back[i].Stops[1].City);
        }
    }
}

[ParquetSerializable]
public sealed partial class Stop
{
    public string? City { get; init; }
    public int? Zip { get; init; }
    public Guid Node { get; init; }
}

[ParquetSerializable]
public sealed partial record TripRow
{
    public int Id { get; init; }
    public List<Stop>? Stops { get; init; }
    public Stop[]? Route { get; init; }
}
