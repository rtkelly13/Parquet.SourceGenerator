using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record SizingModel
{
    [ParquetColumn("id")]
    public int Id { get; init; }
}

/// <summary>
/// Covers how a row group size is chosen. The previous resolution treated the default value 50,000
/// as a sentinel meaning "unset", so an explicit 50,000 was ignored and options silently overrode
/// the more specific method argument.
/// </summary>
public sealed class RowGroupSizingTests
{
    private static List<SizingModel> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => new SizingModel { Id = i }).ToList();

    private static async Task<int> RowGroupCountAsync(
        int? rowGroupSize,
        ParquetSerializerOptions? options,
        int rowCount
    )
    {
        using var stream = new MemoryStream();
        await Rows(rowCount).WriteParquetBatchedAsync(stream, rowGroupSize, options);
        stream.Position = 0;

        // Parquet.Net v6's ParquetReader exposes DisposeAsync only — there is no sync Dispose.
        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream);
        return reader.RowGroupCount;
    }

    [Fact]
    public async Task ExplicitArgumentWinsOverOptions()
    {
        // The explicit argument is the more specific instruction, so it takes precedence. Under the
        // old sentinel logic options won whenever it held anything other than 50,000.
        int groups = await RowGroupCountAsync(
            rowGroupSize: 2,
            options: new ParquetSerializerOptions { RowGroupSize = 10 },
            rowCount: 6
        );

        Assert.Equal(3, groups);
    }

    [Fact]
    public async Task OptionsApplyWhenNoExplicitArgumentIsGiven()
    {
        int groups = await RowGroupCountAsync(
            rowGroupSize: null,
            options: new ParquetSerializerOptions { RowGroupSize = 2 },
            rowCount: 6
        );

        Assert.Equal(3, groups);
    }

    [Fact]
    public async Task ExplicitDefaultSizedRowGroupIsHonouredRatherThanTreatedAsUnset()
    {
        // 50,000 used to double as the "unset" sentinel, so asking for it explicitly while options
        // carried a different value quietly produced the options value instead.
        int groups = await RowGroupCountAsync(
            rowGroupSize: 50_000,
            options: new ParquetSerializerOptions { RowGroupSize = 2 },
            rowCount: 6
        );

        Assert.Equal(1, groups);
    }

    [Fact]
    public async Task NonPositiveRowGroupSizeIsRejectedFromEitherSource()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Rows(2).WriteParquetBatchedAsync(stream, rowGroupSize: 0)
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Rows(2).WriteParquetBatchedAsync(stream, rowGroupSize: -10)
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Rows(2)
                .WriteParquetBatchedAsync(
                    stream,
                    null,
                    new ParquetSerializerOptions { RowGroupSize = 0 }
                )
        );
    }

    [Fact]
    public void DefaultOptionsCannotBeMutatedForTheWholeProcess()
    {
        // Default was a shared singleton with settable properties, so one caller could rewrite the
        // defaults every other serializer in the process would pick up.
        ParquetSerializerOptions first = ParquetSerializerOptions.Default;
        first.RowGroupSize = 7;

        Assert.Equal(50_000, ParquetSerializerOptions.Default.RowGroupSize);
        Assert.NotSame(first, ParquetSerializerOptions.Default);
    }
}
