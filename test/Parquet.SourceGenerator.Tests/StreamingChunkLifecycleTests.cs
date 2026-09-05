using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Parquet;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record StreamingChunkModel
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("text")]
    public string Text { get; init; } = string.Empty;

    [ParquetColumn("optional_text")]
    public string? OptionalText { get; init; }

    [ParquetColumn("data")]
    public byte[] Data { get; init; } = Array.Empty<byte>();

    [ParquetColumn("guid")]
    public Guid GuidVal { get; init; }

    [ParquetColumn("amount")]
    [ParquetDecimal(18, 2)]
    public decimal Amount { get; init; }
}

public class StreamingChunkLifecycleTests
{
    private static async IAsyncEnumerable<StreamingChunkModel> ToAsyncEnumerable(
        IEnumerable<StreamingChunkModel> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task StreamingWriteRemainderChunkLifecycleAndFidelity()
    {
        const int totalCount = 17;
        const int rowGroupSize = 5;

        var items = Enumerable
            .Range(0, totalCount)
            .Select(i => new StreamingChunkModel
            {
                Id = i,
                Text = $"Text_{i}",
                OptionalText = i % 3 == 0 ? null : $"Opt_{i}",
                Data = new byte[] { (byte)(i & 0xFF), (byte)((i * 7) & 0xFF) },
                GuidVal = Guid.NewGuid(),
                Amount = 100.50m + i,
            })
            .ToList();

        using var stream = new MemoryStream();
        await ToAsyncEnumerable(items).WriteParquetAsync(stream, rowGroupSize: rowGroupSize);

        // 1. Verify Parquet low-level row group structure
        stream.Position = 0;
        await using (var reader = await ParquetReader.CreateAsync(stream))
        {
            Assert.Equal(4, reader.RowGroupCount);
            using var rg0 = reader.OpenRowGroupReader(0);
            Assert.Equal(5, rg0.RowCount);
            using var rg1 = reader.OpenRowGroupReader(1);
            Assert.Equal(5, rg1.RowCount);
            using var rg2 = reader.OpenRowGroupReader(2);
            Assert.Equal(5, rg2.RowCount);
            using var rg3 = reader.OpenRowGroupReader(3);
            Assert.Equal(2, rg3.RowCount);
        }

        // 2. Sequential Read
        stream.Position = 0;
        List<StreamingChunkModel> sequentialResults =
            await StreamingChunkModelParquetExtensions.ReadParquetAsync(stream);
        AssertEqualLists(items, sequentialResults);

        // 3. Parallel Read across row groups
        stream.Position = 0;
        List<StreamingChunkModel> parallelResults =
            await StreamingChunkModelParquetExtensions.ReadParquetParallelAsync(stream);
        AssertEqualLists(items, parallelResults);

        // 4. Streaming Read row-group by row-group
        stream.Position = 0;
        var streamedResults = new List<StreamingChunkModel>();
        await foreach (
            var item in StreamingChunkModelParquetExtensions.ReadParquetStreamAsync(stream)
        )
        {
            streamedResults.Add(item);
        }
        AssertEqualLists(items, streamedResults);
    }

    [Fact]
    public async Task StreamingWriteExactMultipleProducesExactRowGroups()
    {
        const int totalCount = 20;
        const int rowGroupSize = 5;

        var items = Enumerable
            .Range(0, totalCount)
            .Select(i => new StreamingChunkModel
            {
                Id = i,
                Text = $"Item_{i}",
                OptionalText = $"Opt_{i}",
                Data = new byte[] { (byte)i },
                GuidVal = Guid.NewGuid(),
                Amount = 10.0m * i,
            })
            .ToList();

        using var stream = new MemoryStream();
        await ToAsyncEnumerable(items).WriteParquetAsync(stream, rowGroupSize: rowGroupSize);

        stream.Position = 0;
        await using (var reader = await ParquetReader.CreateAsync(stream))
        {
            Assert.Equal(4, reader.RowGroupCount);
            for (int r = 0; r < 4; r++)
            {
                using var rg = reader.OpenRowGroupReader(r);
                Assert.Equal(5, rg.RowCount);
            }
        }

        stream.Position = 0;
        List<StreamingChunkModel> readBack =
            await StreamingChunkModelParquetExtensions.ReadParquetAsync(stream);
        AssertEqualLists(items, readBack);
    }

    [Fact]
    public async Task StreamingWriteSingleItemRemainderChunkLifecycle()
    {
        const int totalCount = 11;
        const int rowGroupSize = 5;

        var items = Enumerable
            .Range(0, totalCount)
            .Select(i => new StreamingChunkModel
            {
                Id = i,
                Text = $"Row_{i}",
                OptionalText = null,
                Data = Array.Empty<byte>(),
                GuidVal = Guid.NewGuid(),
                Amount = 1.0m,
            })
            .ToList();

        using var stream = new MemoryStream();
        await ToAsyncEnumerable(items).WriteParquetAsync(stream, rowGroupSize: rowGroupSize);

        stream.Position = 0;
        await using (var reader = await ParquetReader.CreateAsync(stream))
        {
            Assert.Equal(3, reader.RowGroupCount);
            using var rg0 = reader.OpenRowGroupReader(0);
            Assert.Equal(5, rg0.RowCount);
            using var rg1 = reader.OpenRowGroupReader(1);
            Assert.Equal(5, rg1.RowCount);
            using var rg2 = reader.OpenRowGroupReader(2);
            Assert.Equal(1, rg2.RowCount);
        }

        stream.Position = 0;
        List<StreamingChunkModel> readBack =
            await StreamingChunkModelParquetExtensions.ReadParquetParallelAsync(stream);
        AssertEqualLists(items, readBack);
    }

    [Fact]
    public async Task StreamingWriteEmptySequenceCreatesValidEmptyParquetFile()
    {
        var items = Enumerable.Empty<StreamingChunkModel>();

        using var stream = new MemoryStream();
        await ToAsyncEnumerable(items).WriteParquetAsync(stream, rowGroupSize: 10);

        stream.Position = 0;
        await using (var reader = await ParquetReader.CreateAsync(stream))
        {
            Assert.Equal(0, reader.RowGroupCount);
        }

        stream.Position = 0;
        List<StreamingChunkModel> readBack =
            await StreamingChunkModelParquetExtensions.ReadParquetAsync(stream);
        Assert.Empty(readBack);
    }

    [Fact]
    public async Task StreamingWriteMultiRowGroupBufferRecyclingDoesNotLeakStaleData()
    {
        // First row group of 5 items has populated text and binary
        // Second row group (remainder of 2 items) has null text and empty binary
        var items = new List<StreamingChunkModel>
        {
            // Row Group 1 (5 items)
            new()
            {
                Id = 1,
                Text = "NonEmpty1",
                OptionalText = "LongStringForBuffer1",
                Data = new byte[] { 1, 2, 3, 4, 5 },
                GuidVal = Guid.NewGuid(),
                Amount = 12.34m,
            },
            new()
            {
                Id = 2,
                Text = "NonEmpty2",
                OptionalText = "LongStringForBuffer2",
                Data = new byte[] { 6, 7, 8, 9, 10 },
                GuidVal = Guid.NewGuid(),
                Amount = 23.45m,
            },
            new()
            {
                Id = 3,
                Text = "NonEmpty3",
                OptionalText = "LongStringForBuffer3",
                Data = new byte[] { 11, 12, 13, 14, 15 },
                GuidVal = Guid.NewGuid(),
                Amount = 34.56m,
            },
            new()
            {
                Id = 4,
                Text = "NonEmpty4",
                OptionalText = "LongStringForBuffer4",
                Data = new byte[] { 16, 17, 18, 19, 20 },
                GuidVal = Guid.NewGuid(),
                Amount = 45.67m,
            },
            new()
            {
                Id = 5,
                Text = "NonEmpty5",
                OptionalText = "LongStringForBuffer5",
                Data = new byte[] { 21, 22, 23, 24, 25 },
                GuidVal = Guid.NewGuid(),
                Amount = 56.78m,
            },
            // Row Group 2 (remainder 2 items) with nulls and empty arrays
            new()
            {
                Id = 6,
                Text = "",
                OptionalText = null,
                Data = Array.Empty<byte>(),
                GuidVal = Guid.Empty,
                Amount = 0m,
            },
            new()
            {
                Id = 7,
                Text = "",
                OptionalText = null,
                Data = Array.Empty<byte>(),
                GuidVal = Guid.Empty,
                Amount = 0m,
            },
        };

        using var stream = new MemoryStream();
        await ToAsyncEnumerable(items).WriteParquetAsync(stream, rowGroupSize: 5);

        // Verify low level
        stream.Position = 0;
        await using (var reader = await ParquetReader.CreateAsync(stream))
        {
            Assert.Equal(2, reader.RowGroupCount);
            using var rg0 = reader.OpenRowGroupReader(0);
            Assert.Equal(5, rg0.RowCount);
            using var rg1 = reader.OpenRowGroupReader(1);
            Assert.Equal(2, rg1.RowCount);
        }

        // Verify roundtrip read
        stream.Position = 0;
        List<StreamingChunkModel> results =
            await StreamingChunkModelParquetExtensions.ReadParquetParallelAsync(stream);

        AssertEqualLists(items, results);

        // Specifically assert the remainder items did not leak stale buffer contents
        Assert.Equal(string.Empty, results[5].Text);
        Assert.Null(results[5].OptionalText);
        Assert.Empty(results[5].Data);
        Assert.Equal(Guid.Empty, results[5].GuidVal);
        Assert.Equal(0m, results[5].Amount);

        Assert.Equal(string.Empty, results[6].Text);
        Assert.Null(results[6].OptionalText);
        Assert.Empty(results[6].Data);
        Assert.Equal(Guid.Empty, results[6].GuidVal);
        Assert.Equal(0m, results[6].Amount);
    }

    private static void AssertEqualLists(
        List<StreamingChunkModel> expected,
        List<StreamingChunkModel> actual
    )
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Id, actual[i].Id);
            Assert.Equal(expected[i].Text, actual[i].Text);
            Assert.Equal(expected[i].OptionalText, actual[i].OptionalText);
            Assert.Equal(expected[i].Data, actual[i].Data);
            Assert.Equal(expected[i].GuidVal, actual[i].GuidVal);
            Assert.Equal(expected[i].Amount, actual[i].Amount);
        }
    }
}
