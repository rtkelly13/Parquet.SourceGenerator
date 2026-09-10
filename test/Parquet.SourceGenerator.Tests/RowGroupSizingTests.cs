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
/// Covers how a row group size is chosen.
/// <para>
/// Issue #218 gave the knob a single home. It used to exist twice — a <c>rowGroupSize</c> argument
/// on the batched write members and <see cref="ParquetSerializerOptions.RowGroupSize"/> — which
/// needed a precedence rule that was invisible from the signature, and which had already produced
/// one bug: the default 50,000 doubled as an "unset" sentinel, so asking for it explicitly was
/// silently overridden by options. With one home there is no precedence left to get wrong, and the
/// tests that pinned it down are gone with it.
/// </para>
/// </summary>
public sealed class RowGroupSizingTests
{
    private static List<SizingModel> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => new SizingModel { Id = i }).ToList();

    private static async Task<int> RowGroupCountAsync(
        ParquetSerializerOptions? options,
        int rowCount
    )
    {
        using var stream = new MemoryStream();
        await Rows(rowCount).WriteParquetBatchedAsync(stream, options);
        stream.Position = 0;

        // Parquet.Net v6's ParquetReader exposes DisposeAsync only — there is no sync Dispose.
        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream);
        return reader.RowGroupCount;
    }

    [Fact]
    public async Task OptionsRowGroupSizeIsHonoured()
    {
        int groups = await RowGroupCountAsync(
            new ParquetSerializerOptions { RowGroupSize = 2 },
            rowCount: 6
        );

        Assert.Equal(3, groups);
    }

    [Fact]
    public async Task DefaultRowGroupSizeAppliesWhenOptionsAreOmitted()
    {
        int groups = await RowGroupCountAsync(options: null, rowCount: 6);

        Assert.Equal(1, groups);
    }

    [Fact]
    public async Task ExplicitDefaultSizedRowGroupIsHonouredRatherThanTreatedAsUnset()
    {
        // 50,000 used to double as an "unset" sentinel. It is now an ordinary value like any other,
        // but the case is worth keeping: it is the one that exposed the sentinel bug.
        int groups = await RowGroupCountAsync(
            new ParquetSerializerOptions { RowGroupSize = 50_000 },
            rowCount: 6
        );

        Assert.Equal(1, groups);
    }

    [Fact]
    public async Task NonPositiveRowGroupSizeIsRejected()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Rows(2)
                .WriteParquetBatchedAsync(stream, new ParquetSerializerOptions { RowGroupSize = 0 })
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Rows(2)
                .WriteParquetBatchedAsync(
                    stream,
                    new ParquetSerializerOptions { RowGroupSize = -10 }
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
