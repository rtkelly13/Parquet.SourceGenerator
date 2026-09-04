using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

// ── New type test models ───────────────────────────────────────────────────

public enum EventStatus
{
    Pending = 0,
    Active = 1,
    Closed = 2,
}

[ParquetSerializable]
public partial record TypeCoverageRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("created_at")]
    public DateTime CreatedAt { get; init; }

    [ParquetColumn("correlation_id")]
    public Guid CorrelationId { get; init; }

    [ParquetColumn("status")]
    public EventStatus Status { get; init; }

    [ParquetColumn("duration_ms")]
    public TimeSpan Duration { get; init; }
}

[ParquetSerializable]
public partial record CompactTimestampRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("micro_ts")]
    [ParquetTimestamp(ParquetTimestampUnit.Microseconds)]
    public DateTime MicroTs { get; init; }
}

[ParquetSerializable]
public partial record NullableTypeCoverageRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("created_at")]
    public DateTime? CreatedAt { get; init; }

    [ParquetColumn("correlation_id")]
    public Guid? CorrelationId { get; init; }

    [ParquetColumn("status")]
    public EventStatus? Status { get; init; }
}

// ── Tests ──────────────────────────────────────────────────────────────────

public sealed class TypeCoverageTests
{
    [Fact]
    public async Task DateTimeRoundtripsCorrectly()
    {
        var now = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var items = new List<TypeCoverageRecord>
        {
            new()
            {
                Id = 1,
                CreatedAt = now,
                CorrelationId = Guid.Empty,
                Status = EventStatus.Active,
                Duration = TimeSpan.FromSeconds(30),
            },
        };

        var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Single(result);
        // DateTime precision in Parquet Impala format is milliseconds
        Assert.Equal(
            now.Ticks / TimeSpan.TicksPerMillisecond,
            result[0].CreatedAt.Ticks / TimeSpan.TicksPerMillisecond
        );
    }

    [Fact]
    public async Task CompactMicrosecondTimestampRoundtripsCorrectly()
    {
        var now = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var items = new List<CompactTimestampRecord>
        {
            new() { Id = 1, MicroTs = now },
        };

        var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);
        byte[] bytes = stream.ToArray();

        // Test zero-copy ReadOnlyMemory overload as well!
        ReadOnlyMemory<byte> mem = bytes;
        var result = await CompactTimestampRecordParquetExtensions.ReadParquetAsync(mem);

        Assert.Single(result);
        Assert.Equal(
            now.Ticks / TimeSpan.TicksPerMillisecond,
            result[0].MicroTs.Ticks / TimeSpan.TicksPerMillisecond
        );
    }

    [Fact]
    public async Task CustomOptionsRoundtripCorrectly()
    {
        var items = Enumerable
            .Range(0, 100)
            .Select(i => new TypeCoverageRecord
            {
                Id = i,
                CreatedAt = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid(),
                Status = EventStatus.Active,
                Duration = TimeSpan.FromSeconds(i),
            })
            .ToList();

        var options = new ParquetSerializerOptions
        {
            RowGroupSize = 25,
            MaxDegreeOfParallelism = 2,
            CompressionMethod = ParquetCompressionMethod.Snappy,
        };

        var stream = new MemoryStream();
        await items.WriteParquetBatchedAsync(stream, options: options);
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetParallelAsync(
            stream,
            options: options
        );

        Assert.Equal(100, result.Count);
        Assert.Equal(items[50].Id, result[50].Id);
    }

    [Fact]
    public async Task AsyncEnumerableStreamingRoundtripsCorrectly()
    {
        static async IAsyncEnumerable<TypeCoverageRecord> GenerateAsyncStream()
        {
            for (int i = 0; i < 50; i++)
            {
                await Task.Yield();
                yield return new TypeCoverageRecord
                {
                    Id = i,
                    CreatedAt = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid(),
                    Status = (EventStatus)(i % 3),
                    Duration = TimeSpan.FromMilliseconds(i * 50),
                };
            }
        }

        var stream = new MemoryStream();
        await GenerateAsyncStream().WriteParquetAsync(stream, rowGroupSize: 10);
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(50, result.Count);
        Assert.Equal(0, result[0].Id);
        Assert.Equal(49, result[49].Id);
    }

    [Fact]
    public async Task GuidRoundtripsCorrectly()
    {
        var id = Guid.NewGuid();
        var items = new List<TypeCoverageRecord>
        {
            new()
            {
                Id = 1,
                CreatedAt = DateTime.UtcNow,
                CorrelationId = id,
                Status = EventStatus.Pending,
                Duration = TimeSpan.Zero,
            },
        };

        var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Single(result);
        Assert.Equal(id, result[0].CorrelationId);
    }

    [Fact]
    public async Task EnumRoundtripsCorrectly()
    {
        var items = Enum.GetValues<EventStatus>()
            .Select(
                (s, idx) =>
                    new TypeCoverageRecord
                    {
                        Id = idx,
                        CreatedAt = DateTime.UtcNow,
                        CorrelationId = Guid.NewGuid(),
                        Status = s,
                        Duration = TimeSpan.FromMinutes(idx),
                    }
            )
            .ToList();

        var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(items.Count, result.Count);
        for (int i = 0; i < items.Count; i++)
            Assert.Equal(items[i].Status, result[i].Status);
    }

    [Fact]
    public async Task TimeSpanRoundtripsCorrectly()
    {
        var duration = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(15);
        var items = new List<TypeCoverageRecord>
        {
            new()
            {
                Id = 1,
                CreatedAt = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid(),
                Status = EventStatus.Active,
                Duration = duration,
            },
        };

        var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Single(result);
        // TimeSpan in Parquet MilliSeconds format — verify millisecond precision
        Assert.Equal(
            duration.Ticks / TimeSpan.TicksPerMillisecond,
            result[0].Duration.Ticks / TimeSpan.TicksPerMillisecond
        );
    }

    [Fact]
    public async Task MultipleRowGroupsBatchedRoundtripsCorrectly()
    {
        var items = Enumerable
            .Range(0, 1_000)
            .Select(i => new TypeCoverageRecord
            {
                Id = i,
                CreatedAt = DateTime.UtcNow.AddSeconds(i),
                CorrelationId = Guid.NewGuid(),
                Status = (EventStatus)(i % 3),
                Duration = TimeSpan.FromMilliseconds(i * 100),
            })
            .ToList();

        var stream = new MemoryStream();
        // Write in 3 row groups (rowGroupSize=333 → 3 groups + tail)
        await ((IEnumerable<TypeCoverageRecord>)items).WriteParquetBatchedAsync(
            stream,
            rowGroupSize: 333
        );
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(items.Count, result.Count);
        Assert.Equal(items[500].Id, result[500].Id);
        Assert.Equal(items[500].CorrelationId, result[500].CorrelationId);
        Assert.Equal(items[500].Status, result[500].Status);
    }

    [Fact]
    public async Task ReadParquetParallelAsyncRoundtripsCorrectly()
    {
        var items = Enumerable
            .Range(0, 1_000)
            .Select(i => new TypeCoverageRecord
            {
                Id = i,
                CreatedAt = DateTime.UtcNow.AddSeconds(i),
                CorrelationId = Guid.NewGuid(),
                Status = (EventStatus)(i % 3),
                Duration = TimeSpan.FromMilliseconds(i * 100),
            })
            .ToList();

        var stream = new MemoryStream();
        await ((IEnumerable<TypeCoverageRecord>)items).WriteParquetBatchedAsync(
            stream,
            rowGroupSize: 250
        );
        stream.Position = 0;

        var result = await TypeCoverageRecordParquetExtensions.ReadParquetParallelAsync(
            stream,
            maxDegreeOfParallelism: 4
        );

        Assert.Equal(items.Count, result.Count);
        Assert.Equal(items[0].Id, result[0].Id);
        Assert.Equal(items[500].Id, result[500].Id);
        Assert.Equal(items[999].Id, result[999].Id);
        Assert.Equal(items[500].CorrelationId, result[500].CorrelationId);
    }

    [Fact]
    public async Task NullableTypesRoundtripCorrectly()
    {
        var id1 = Guid.NewGuid();
        var items = new List<NullableTypeCoverageRecord>
        {
            new()
            {
                Id = 1,
                CreatedAt = null,
                CorrelationId = null,
                Status = null,
            },
            new()
            {
                Id = 2,
                CreatedAt = DateTime.UtcNow,
                CorrelationId = id1,
                Status = EventStatus.Closed,
            },
        };

        var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);
        stream.Position = 0;

        var result = await NullableTypeCoverageRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(2, result.Count);
        Assert.Null(result[0].CreatedAt);
        Assert.Null(result[0].CorrelationId);
        Assert.Null(result[0].Status);
        Assert.Equal(id1, result[1].CorrelationId);
        Assert.Equal(EventStatus.Closed, result[1].Status);
    }
}
