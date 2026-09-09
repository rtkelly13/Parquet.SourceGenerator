using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Parquet;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record NullBypassRecord
{
    [ParquetColumn("optional_int")]
    public int? OptionalInt { get; init; }

    [ParquetColumn("optional_string")]
    public string? OptionalString { get; init; }

    [ParquetColumn("optional_bytes")]
    public byte[]? OptionalBytes { get; init; }

    [ParquetColumn("optional_guid")]
    public Guid? OptionalGuid { get; init; }

    [ParquetColumn("optional_date_time")]
    public DateTime? OptionalDateTime { get; init; }

    [ParquetColumn("optional_duration")]
    public TimeSpan? OptionalDuration { get; init; }

    [ParquetColumn("optional_status")]
    public EventStatus? OptionalStatus { get; init; }
}

public sealed class NullBypassReadTests
{
    [Fact]
    public async Task AllNullRowGroupRoundtripsThroughEveryReadPath()
    {
        var items = Enumerable.Range(0, 4).Select(_ => new NullBypassRecord()).ToList();
        byte[] parquet = await WriteAsync(items);

        await using (var reader = await ParquetReader.CreateAsync(new MemoryStream(parquet)))
        {
            using var rowGroup = reader.OpenRowGroupReader(0);
            Assert.Equal(4, rowGroup.RowCount);
            foreach (var field in reader.Schema.DataFields)
            {
                Assert.Equal(4, rowGroup.GetStatistics(field)?.NullCount);
            }
        }

        List<NullBypassRecord> sequential = await ReadSequentialAsync(parquet);
        NullBypassRecord[] array = await NullBypassRecordParquetExtensions.ReadParquetArrayAsync(
            new MemoryStream(parquet)
        );
        List<NullBypassRecord> parallel =
            await NullBypassRecordParquetExtensions.ReadParquetParallelAsync(
                new MemoryStream(parquet),
                maxDegreeOfParallelism: 2
            );
        NullBypassRecord[] parallelArray =
            await NullBypassRecordParquetExtensions.ReadParquetParallelArrayAsync(
                parquet,
                maxDegreeOfParallelism: 2
            );

        var streamed = new List<NullBypassRecord>();
        await foreach (
            NullBypassRecord item in NullBypassRecordParquetExtensions.ReadParquetStreamAsync(
                new MemoryStream(parquet)
            )
        )
        {
            streamed.Add(item);
        }

        AssertAllNull(sequential);
        AssertAllNull(array);
        AssertAllNull(parallel);
        AssertAllNull(parallelArray);
        AssertAllNull(streamed);
    }

    [Fact]
    public async Task MixedNullableValuesRoundtripWithoutChangingNullSemantics()
    {
        Guid firstGuid = Guid.NewGuid();
        Guid secondGuid = Guid.NewGuid();
        DateTime firstDate = new(2024, 6, 15, 12, 30, 0, 123, DateTimeKind.Utc);
        TimeSpan firstDuration = TimeSpan.FromMilliseconds(1234);
        var expected = new List<NullBypassRecord>
        {
            new()
            {
                OptionalInt = 7,
                OptionalString = "first",
                OptionalBytes = new byte[] { 1, 2 },
                OptionalGuid = firstGuid,
                OptionalDateTime = firstDate,
                OptionalDuration = firstDuration,
                OptionalStatus = EventStatus.Active,
            },
            new()
            {
                OptionalString = string.Empty,
                OptionalBytes = Array.Empty<byte>(),
                OptionalGuid = secondGuid,
                OptionalStatus = EventStatus.Closed,
            },
            new()
            {
                OptionalInt = -3,
                OptionalDateTime = firstDate.AddDays(1),
                OptionalDuration = TimeSpan.Zero,
            },
        };

        byte[] parquet = await WriteAsync(expected);
        List<NullBypassRecord> actual = await ReadSequentialAsync(parquet);

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].OptionalInt, actual[i].OptionalInt);
            Assert.Equal(expected[i].OptionalString, actual[i].OptionalString);
            Assert.Equal(expected[i].OptionalBytes, actual[i].OptionalBytes);
            Assert.Equal(expected[i].OptionalGuid, actual[i].OptionalGuid);
            Assert.Equal(
                expected[i].OptionalDateTime?.Ticks / TimeSpan.TicksPerMillisecond,
                actual[i].OptionalDateTime?.Ticks / TimeSpan.TicksPerMillisecond
            );
            Assert.Equal(expected[i].OptionalDuration, actual[i].OptionalDuration);
            Assert.Equal(expected[i].OptionalStatus, actual[i].OptionalStatus);
        }
    }

    [Fact]
    public async Task ZeroNullRowGroupRoundtripsThroughEveryReadPath()
    {
        // Every column is populated, so each chunk statistic reports NullCount == 0 and the
        // generated reader takes the dense (#150) lane instead of the nullable staging buffer.
        var expected = Enumerable
            .Range(0, 64)
            .Select(i => new NullBypassRecord
            {
                OptionalInt = i * 7,
                OptionalString = "value-" + i.ToString(CultureInfo.InvariantCulture),
                OptionalBytes = new byte[] { (byte)i, (byte)(i + 1) },
                OptionalGuid = Guid.Parse(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:x8}-0000-0000-0000-000000000000",
                        i
                    )
                ),
                OptionalDateTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(
                    i
                ),
                OptionalDuration = TimeSpan.FromMilliseconds(i * 13),
                OptionalStatus = i % 2 == 0 ? EventStatus.Active : EventStatus.Closed,
            })
            .ToList();

        byte[] parquet = await WriteAsync(expected);

        await using (var reader = await ParquetReader.CreateAsync(new MemoryStream(parquet)))
        {
            using var rowGroup = reader.OpenRowGroupReader(0);
            foreach (var field in reader.Schema.DataFields)
            {
                Assert.Equal(0, rowGroup.GetStatistics(field)?.NullCount);
            }
        }

        AssertMatches(expected, await ReadSequentialAsync(parquet));
        AssertMatches(
            expected,
            await NullBypassRecordParquetExtensions.ReadParquetArrayAsync(new MemoryStream(parquet))
        );
        AssertMatches(
            expected,
            await NullBypassRecordParquetExtensions.ReadParquetParallelAsync(
                new MemoryStream(parquet),
                maxDegreeOfParallelism: 2
            )
        );
        AssertMatches(
            expected,
            await NullBypassRecordParquetExtensions.ReadParquetParallelArrayAsync(
                parquet,
                maxDegreeOfParallelism: 2
            )
        );

        var streamed = new List<NullBypassRecord>();
        await foreach (
            NullBypassRecord item in NullBypassRecordParquetExtensions.ReadParquetStreamAsync(
                new MemoryStream(parquet)
            )
        )
        {
            streamed.Add(item);
        }
        AssertMatches(expected, streamed);
    }

    [Fact]
    public async Task ZeroNullAndPartiallyNullRowGroupsDecodeIndependently()
    {
        // Row group 0 has no nulls (fast path), row group 1 is partially null (standard decode),
        // row group 2 is entirely null (all-null bypass). One reader pass must handle all three.
        var items = new List<NullBypassRecord>
        {
            new() { OptionalInt = 1, OptionalStatus = EventStatus.Active },
            new() { OptionalInt = 2, OptionalStatus = EventStatus.Closed },
            new() { OptionalInt = 3, OptionalStatus = EventStatus.Active },
            new() { OptionalInt = null, OptionalStatus = EventStatus.Closed },
            new() { OptionalInt = null, OptionalStatus = null },
            new() { OptionalInt = null, OptionalStatus = null },
        };

        using var stream = new MemoryStream();
        await items.WriteParquetAsync(stream, new ParquetSerializerOptions { RowGroupSize = 2 });
        byte[] parquet = stream.ToArray();

        List<NullBypassRecord> actual = await ReadSequentialAsync(parquet);
        AssertMatches(items, actual);
    }

    private static void AssertMatches(
        List<NullBypassRecord> expected,
        IEnumerable<NullBypassRecord> actualItems
    )
    {
        var actual = actualItems.ToList();
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].OptionalInt, actual[i].OptionalInt);
            Assert.Equal(expected[i].OptionalString, actual[i].OptionalString);
            Assert.Equal(expected[i].OptionalBytes, actual[i].OptionalBytes);
            Assert.Equal(expected[i].OptionalGuid, actual[i].OptionalGuid);
            Assert.Equal(
                expected[i].OptionalDateTime?.Ticks / TimeSpan.TicksPerMillisecond,
                actual[i].OptionalDateTime?.Ticks / TimeSpan.TicksPerMillisecond
            );
            Assert.Equal(expected[i].OptionalDuration, actual[i].OptionalDuration);
            Assert.Equal(expected[i].OptionalStatus, actual[i].OptionalStatus);
        }
    }

    private static async Task<byte[]> WriteAsync(List<NullBypassRecord> items)
    {
        using var stream = new MemoryStream();
        await items.WriteParquetAsync(
            stream,
            new ParquetSerializerOptions { RowGroupSize = items.Count }
        );
        return stream.ToArray();
    }

    private static async Task<List<NullBypassRecord>> ReadSequentialAsync(byte[] parquet)
    {
        return await NullBypassRecordParquetExtensions.ReadParquetAsync(new MemoryStream(parquet));
    }

    private static void AssertAllNull(IEnumerable<NullBypassRecord> items)
    {
        foreach (NullBypassRecord item in items)
        {
            Assert.Null(item.OptionalInt);
            Assert.Null(item.OptionalString);
            Assert.Null(item.OptionalBytes);
            Assert.Null(item.OptionalGuid);
            Assert.Null(item.OptionalDateTime);
            Assert.Null(item.OptionalDuration);
            Assert.Null(item.OptionalStatus);
        }
    }
}
