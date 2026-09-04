using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

// Two models over the same three columns, declared in opposite orders. Reading a file written by
// one using the reader generated for the other forces the generated schema resolution down its
// name-matching path, which nothing else in the suite exercises: every other test reads a file
// whose column order already matches the model, so it resolves on the index fast path.

[ParquetSerializable]
public partial record ForwardOrder
{
    [ParquetColumn("alpha", Order = 1)]
    public int Alpha { get; init; }

    [ParquetColumn("beta", Order = 2)]
    public string Beta { get; init; } = string.Empty;

    [ParquetColumn("gamma", Order = 3)]
    public double Gamma { get; init; }
}

[ParquetSerializable]
public partial record ReversedOrder
{
    [ParquetColumn("gamma", Order = 1)]
    public double Gamma { get; init; }

    [ParquetColumn("beta", Order = 2)]
    public string Beta { get; init; } = string.Empty;

    [ParquetColumn("alpha", Order = 3)]
    public int Alpha { get; init; }
}

[ParquetSerializable]
public partial record PartialOrder
{
    [ParquetColumn("alpha", Order = 1)]
    public int Alpha { get; init; }

    [ParquetColumn("beta", Order = 2)]
    public string Beta { get; init; } = string.Empty;
}

public sealed class SchemaFieldResolutionTests
{
    [Fact]
    public async Task ColumnsAreMatchedByNameWhenFileOrderDiffersFromModelOrder()
    {
        var written = new List<ForwardOrder>
        {
            new()
            {
                Alpha = 1,
                Beta = "one",
                Gamma = 1.5,
            },
            new()
            {
                Alpha = 2,
                Beta = "two",
                Gamma = 2.5,
            },
            new()
            {
                Alpha = 3,
                Beta = "three",
                Gamma = 3.5,
            },
        };

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        stream.Position = 0;

        // The file's columns are [alpha, beta, gamma]; this reader expects [gamma, beta, alpha].
        // Index 0 and index 2 therefore miss and must be resolved by name, while index 1 (beta)
        // still hits on position — so a single read covers both paths and the reuse between misses.
        List<ReversedOrder> read = await ReversedOrderParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(3, read.Count);
        for (int i = 0; i < written.Count; i++)
        {
            // If resolution fell back to position rather than name, Alpha and Gamma would be
            // transposed here rather than merely wrong.
            Assert.Equal(written[i].Alpha, read[i].Alpha);
            Assert.Equal(written[i].Beta, read[i].Beta);
            Assert.Equal(written[i].Gamma, read[i].Gamma);
        }
    }

    [Fact]
    public async Task ParallelReaderAlsoMatchesColumnsByName()
    {
        // The parallel reader emits its own copy of the resolution logic, so it needs its own check.
        var written = new List<ForwardOrder>();
        for (int i = 0; i < 50; i++)
        {
            written.Add(
                new ForwardOrder
                {
                    Alpha = i,
                    Beta = $"row_{i}",
                    Gamma = i * 0.25,
                }
            );
        }

        using var stream = new MemoryStream();
        await written.WriteParquetBatchedAsync(stream, rowGroupSize: 10);
        stream.Position = 0;

        List<ReversedOrder> read = await ReversedOrderParquetExtensions.ReadParquetParallelAsync(
            stream
        );

        Assert.Equal(written.Count, read.Count);
        for (int i = 0; i < written.Count; i++)
        {
            Assert.Equal(written[i].Alpha, read[i].Alpha);
            Assert.Equal(written[i].Beta, read[i].Beta);
            Assert.Equal(written[i].Gamma, read[i].Gamma);
        }
    }

    [Fact]
    public async Task MatchingOrderStillRoundtripsOnTheFastPath()
    {
        // Guards the common case against a regression in the resolver's index check.
        var written = new List<ForwardOrder>
        {
            new()
            {
                Alpha = 7,
                Beta = "seven",
                Gamma = 7.75,
            },
        };

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        stream.Position = 0;

        List<ForwardOrder> read = await ForwardOrderParquetExtensions.ReadParquetAsync(stream);

        Assert.Single(read);
        Assert.Equal(7, read[0].Alpha);
        Assert.Equal("seven", read[0].Beta);
        Assert.Equal(7.75, read[0].Gamma);
    }

    [Fact]
    public async Task MissingRequiredColumnThrowsDescriptiveInvalidDataException()
    {
        // Write file with only alpha and beta, omitting required gamma
        var partialList = new List<PartialOrder>
        {
            new() { Alpha = 1, Beta = "b" },
        };

        using var stream = new MemoryStream();
        await partialList.WriteParquetAsync(stream);
        stream.Position = 0;

        // ForwardOrder requires 'gamma' (non-nullable double). Reading partial stream should throw InvalidDataException.
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ForwardOrderParquetExtensions.ReadParquetAsync(stream)
        );
        Assert.Contains(
            "Required column 'gamma' was not found in the Parquet file schema",
            ex.Message
        );
    }
}
