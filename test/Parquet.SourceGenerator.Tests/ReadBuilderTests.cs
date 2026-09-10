using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record BuilderOrder
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string? Name { get; init; }
}

/// <summary>
/// The generated read entry point and builder structs (issue #217).
/// </summary>
public sealed class ReadBuilderTests
{
    private const int RowsPerGroup = 50;

    private static List<BuilderOrder> Rows(int count) =>
        Enumerable
            .Range(1, count)
            .Select(i => new BuilderOrder { Id = i, Name = $"row-{i}" })
            .ToList();

    private static async Task<byte[]> WriteAsync(int count)
    {
        using var stream = new MemoryStream();
        await Rows(count)
            .WriteParquetBatchedAsync(
                stream,
                new ParquetSerializerOptions { RowGroupSize = RowsPerGroup }
            );
        return stream.ToArray();
    }

    [Fact]
    public async Task StreamSourceMaterializesEveryShape()
    {
        byte[] bytes = await WriteAsync(120);

        using var forList = new MemoryStream(bytes);
        List<BuilderOrder> list = await BuilderOrderParquet.From(forList).ToListAsync();

        using var forArray = new MemoryStream(bytes);
        BuilderOrder[] array = await BuilderOrderParquet.From(forArray).ToArrayAsync();

        using var forStream = new MemoryStream(bytes);
        var streamed = new List<BuilderOrder>();
        await foreach (var item in BuilderOrderParquet.From(forStream).AsAsyncEnumerable())
        {
            streamed.Add(item);
        }

        Assert.Equal(120, list.Count);
        Assert.Equal(Enumerable.Range(1, 120), list.Select(r => r.Id));
        Assert.Equal(list, array);
        Assert.Equal(list, streamed);
    }

    [Fact]
    public async Task MemorySourceMaterializesEveryShapeIncludingParallel()
    {
        byte[] bytes = await WriteAsync(120);
        var memory = new ReadOnlyMemory<byte>(bytes);

        List<BuilderOrder> sequential = await BuilderOrderParquet.From(memory).ToListAsync();
        BuilderOrder[] parallel = await BuilderOrderParquet.From(memory).Parallel(2).ToArrayAsync();

        Assert.Equal(120, sequential.Count);
        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public async Task OptionsFlowThroughTheBuilder()
    {
        byte[] bytes = await WriteAsync(60);
        var options = new ParquetSerializerOptions { DeduplicateStrings = true };

        List<BuilderOrder> read = await BuilderOrderParquet
            .From(new ReadOnlyMemory<byte>(bytes))
            .WithOptions(options)
            .ToListAsync();

        Assert.Equal(60, read.Count);
    }

    [Fact]
    public async Task WhereAppliesTheSamePruningFromEitherSource()
    {
        // The flat methods grew `predicate` on the stream overloads of ReadParquetAsync and
        // ReadParquetArrayAsync but never on the buffer ones, so buffer + List and buffer + array
        // were the two cells of the grid where pushdown was simply unavailable (defect 3 in
        // docs/17). The builder routes those through the streaming overload that does accept a
        // predicate, so the same filter is reachable from every source.
        byte[] bytes = await WriteAsync(200);

        using var streamSource = new MemoryStream(bytes);
        List<BuilderOrder> fromStream = await BuilderOrderParquet
            .From(streamSource)
            .Where(m => m.Id.Min >= 100)
            .ToListAsync();

        List<BuilderOrder> fromMemory = await BuilderOrderParquet
            .From(new ReadOnlyMemory<byte>(bytes))
            .Where(m => m.Id.Min >= 100)
            .ToListAsync();

        Assert.NotEmpty(fromStream);
        Assert.Equal(fromStream, fromMemory);
        Assert.All(fromStream, r => Assert.True(r.Id >= 100 - RowsPerGroup));
    }

    [Fact]
    public async Task FilteredArrayAndEnumerableAgreeWithFilteredList()
    {
        byte[] bytes = await WriteAsync(200);
        var memory = new ReadOnlyMemory<byte>(bytes);

        List<BuilderOrder> list = await BuilderOrderParquet
            .From(memory)
            .Where(m => m.Id.Min >= 100)
            .ToListAsync();
        BuilderOrder[] array = await BuilderOrderParquet
            .From(memory)
            .Where(m => m.Id.Min >= 100)
            .ToArrayAsync();

        var streamed = new List<BuilderOrder>();
        await foreach (
            var item in BuilderOrderParquet
                .From(memory)
                .Where(m => m.Id.Min >= 100)
                .AsAsyncEnumerable()
        )
        {
            streamed.Add(item);
        }

        Assert.Equal(list, array);
        Assert.Equal(list, streamed);
    }

    [Fact]
    public void EveryBuilderIsAReadOnlyStructSoComposingAReadAllocatesNothing()
    {
        foreach (
            Type type in new[]
            {
                typeof(BuilderOrderParquetStreamSource),
                typeof(BuilderOrderParquetMemorySource),
                typeof(BuilderOrderParquetFilteredSource),
                typeof(BuilderOrderParquetParallelSource),
            }
        )
        {
            Assert.True(type.IsValueType, $"{type.Name} should be a struct.");
            Assert.True(
                type.GetCustomAttributes().Any(a => a.GetType().Name == "IsReadOnlyAttribute"),
                $"{type.Name} should be readonly."
            );
        }
    }

    [Fact]
    public void UnsupportedCombinationsAreAbsentRatherThanThrowing()
    {
        // The grid has holes — a stream cannot be read by concurrent readers, and no parallel
        // reader accepts a predicate. Those cells are absent from the type rather than present and
        // throwing, so calling one is a compile error instead of a runtime surprise. Asserting the
        // absence keeps a future refactor from quietly reintroducing a throwing member.
        Assert.Null(typeof(BuilderOrderParquetStreamSource).GetMethod("Parallel"));
        Assert.Null(typeof(BuilderOrderParquetFilteredSource).GetMethod("Parallel"));
        Assert.Null(typeof(BuilderOrderParquetParallelSource).GetMethod("Where"));

        // Nor is there a parallel streaming or columnar-batch reader to delegate to.
        Assert.Null(typeof(BuilderOrderParquetParallelSource).GetMethod("AsAsyncEnumerable"));
        Assert.Null(typeof(BuilderOrderParquetParallelSource).GetMethod("Batches"));
        Assert.Null(typeof(BuilderOrderParquetFilteredSource).GetMethod("Batches"));
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => BuilderOrderParquet.From((Stream)null!));
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() =>
            BuilderOrderParquet.From(stream).WithOptions(null!)
        );
        Assert.Throws<ArgumentNullException>(() => BuilderOrderParquet.From(stream).Where(null!));
    }
}
