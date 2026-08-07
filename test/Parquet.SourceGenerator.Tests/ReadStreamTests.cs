using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record StreamTestModel
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ReadStreamTests
{
    [Fact]
    public async Task ReadParquetStreamAsyncStreamsItemsCorrectly()
    {
        var written = new List<StreamTestModel>
        {
            new() { Id = 1, Name = "Item_1" },
            new() { Id = 2, Name = "Item_2" },
            new() { Id = 3, Name = "Item_3" },
        };

        using var stream = new MemoryStream();
        await written.WriteParquetBatchedAsync(stream, rowGroupSize: 2);
        stream.Position = 0;

        var readItems = new List<StreamTestModel>();
        await foreach (var item in StreamTestModelParquetExtensions.ReadParquetStreamAsync(stream))
        {
            readItems.Add(item);
        }

        Assert.Equal(3, readItems.Count);
        Assert.Equal(1, readItems[0].Id);
        Assert.Equal("Item_1", readItems[0].Name);
        Assert.Equal(2, readItems[1].Id);
        Assert.Equal("Item_2", readItems[1].Name);
        Assert.Equal(3, readItems[2].Id);
        Assert.Equal("Item_3", readItems[2].Name);
    }
}
