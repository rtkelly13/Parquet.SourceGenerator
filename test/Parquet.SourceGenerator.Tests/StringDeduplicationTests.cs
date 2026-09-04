using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record CategoricalRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("category")]
    public string Category { get; init; } = string.Empty;

    [ParquetColumn("tag")]
    public string? Tag { get; init; }
}

[ParquetSerializable]
public partial record ExplicitDeduplicateRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("department", Deduplicate = true)]
    public string Department { get; init; } = string.Empty;

    [ParquetColumn("notes")]
    public string Notes { get; init; } = string.Empty;
}

public sealed class StringDeduplicationTests
{
    private static async Task<byte[]> CreateCategoricalParquetBytesAsync(int count = 200)
    {
        var items = new List<CategoricalRecord>(count);
        for (int i = 0; i < count; i++)
        {
            items.Add(
                new CategoricalRecord
                {
                    Id = i,
                    Category = (i % 2 == 0) ? "Electronics" : "Clothing",
                    Tag = (i % 3 == 0) ? "OnSale" : (i % 3 == 1 ? "Clearance" : null),
                }
            );
        }

        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);
        return ms.ToArray();
    }

    private static async Task<byte[]> CreateExplicitDeduplicateParquetBytesAsync(int count = 200)
    {
        var items = new List<ExplicitDeduplicateRecord>(count);
        for (int i = 0; i < count; i++)
        {
            items.Add(
                new ExplicitDeduplicateRecord
                {
                    Id = i,
                    Department = (i % 2 == 0) ? "Engineering" : "Marketing",
                    Notes = "Standard Note",
                }
            );
        }

        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task DeduplicateStringsWhenTrueCanonicalizesIdenticalStrings()
    {
        byte[] bytes = await CreateCategoricalParquetBytesAsync(200);

        var options = new ParquetSerializerOptions { DeduplicateStrings = true };
        var results = await CategoricalRecordParquetExtensions.ReadParquetArrayAsync(
            bytes,
            options
        );

        Assert.Equal(200, results.Length);

        // Find items in the same category
        var electronics1 = results[0];
        var electronics2 = results[2];
        Assert.Equal("Electronics", electronics1.Category);
        Assert.Equal("Electronics", electronics2.Category);
        Assert.True(
            object.ReferenceEquals(electronics1.Category, electronics2.Category),
            "Identical strings should share reference equality when DeduplicateStrings = true"
        );

        var clothing1 = results[1];
        var clothing2 = results[3];
        Assert.Equal("Clothing", clothing1.Category);
        Assert.Equal("Clothing", clothing2.Category);
        Assert.True(
            object.ReferenceEquals(clothing1.Category, clothing2.Category),
            "Identical strings should share reference equality when DeduplicateStrings = true"
        );

        // Nullable strings
        var sale1 = results[0];
        var sale2 = results[6];
        Assert.Equal("OnSale", sale1.Tag);
        Assert.Equal("OnSale", sale2.Tag);
        Assert.True(
            object.ReferenceEquals(sale1.Tag, sale2.Tag),
            "Nullable identical strings should share reference equality when DeduplicateStrings = true"
        );
    }

    [Fact]
    public async Task DeduplicateStringsWhenFalseAllocatesIndependentStrings()
    {
        byte[] bytes = await CreateCategoricalParquetBytesAsync(200);

        var options = new ParquetSerializerOptions { DeduplicateStrings = false };
        var results = await CategoricalRecordParquetExtensions.ReadParquetArrayAsync(
            bytes,
            options
        );

        Assert.Equal(200, results.Length);

        var electronics1 = results[0];
        var electronics2 = results[2];
        Assert.Equal(electronics1.Category, electronics2.Category);
        Assert.False(
            object.ReferenceEquals(electronics1.Category, electronics2.Category),
            "Without deduplication, Parquet.Net emits distinct allocated string instances"
        );
    }

    [Fact]
    public async Task ParquetColumnAttributeDeduplicateTrueDeduplicatesEvenWhenGlobalOptionIsFalse()
    {
        byte[] bytes = await CreateExplicitDeduplicateParquetBytesAsync(200);

        // Global DeduplicateStrings = false
        var options = new ParquetSerializerOptions { DeduplicateStrings = false };
        var results = await ExplicitDeduplicateRecordParquetExtensions.ReadParquetArrayAsync(
            bytes,
            options
        );

        Assert.Equal(200, results.Length);

        // Department has [ParquetColumn(Deduplicate = true)]
        var dept1 = results[0];
        var dept2 = results[2];
        Assert.Equal("Engineering", dept1.Department);
        Assert.Equal("Engineering", dept2.Department);
        Assert.True(
            object.ReferenceEquals(dept1.Department, dept2.Department),
            "Column decorated with [ParquetColumn(Deduplicate = true)] MUST be deduplicated even if global option is false"
        );

        // Notes does NOT have Deduplicate = true, so with global option = false, strings remain separate instances
        Assert.Equal(dept1.Notes, dept2.Notes);
        Assert.False(
            object.ReferenceEquals(dept1.Notes, dept2.Notes),
            "Undecorated column should NOT be deduplicated when global option is false"
        );
    }

    [Fact]
    public async Task DeduplicateStringsWorksWithStreamingAsyncEnumerable()
    {
        byte[] bytes = await CreateCategoricalParquetBytesAsync(100);

        using var stream = new MemoryStream(bytes);
        var options = new ParquetSerializerOptions { DeduplicateStrings = true };

        var items = new List<CategoricalRecord>();
        await foreach (
            var item in CategoricalRecordParquetExtensions.ReadParquetStreamAsync(stream, options)
        )
        {
            items.Add(item);
        }

        Assert.Equal(100, items.Count);
        Assert.True(
            object.ReferenceEquals(items[0].Category, items[2].Category),
            "Streaming reader should deduplicate strings across row group items"
        );
    }

    [Fact]
    public async Task DeduplicateStringsWorksWithParallelArrayReader()
    {
        byte[] bytes = await CreateCategoricalParquetBytesAsync(100);

        var options = new ParquetSerializerOptions { DeduplicateStrings = true };
        var results = await CategoricalRecordParquetExtensions.ReadParquetParallelArrayAsync(
            bytes,
            maxDegreeOfParallelism: 2,
            options: options
        );

        Assert.Equal(100, results.Length);
        Assert.True(
            object.ReferenceEquals(results[0].Category, results[2].Category),
            "Parallel reader should deduplicate strings within workers"
        );
    }
}
