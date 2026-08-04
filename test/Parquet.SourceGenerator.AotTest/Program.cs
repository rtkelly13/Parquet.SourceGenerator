using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Parquet.SourceGenerator;

namespace Parquet.SourceGenerator.AotTest;

[ParquetSerializable]
public sealed partial record AotRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string Name { get; init; } = string.Empty;
}

internal static class Program
{
    private static async Task Main()
    {
        Console.WriteLine("🚀 Running Native AOT Serialization Test...");

        var items = new List<AotRecord>
        {
            new() { Id = 1, Name = "AOT_User_1" },
            new() { Id = 2, Name = "AOT_User_2" }
        };

        using var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);

        stream.Position = 0;
        List<AotRecord> readItems = await AotRecordParquetExtensions.ReadParquetAsync(stream);

        if (readItems.Count != 2 || readItems[0].Name != "AOT_User_1")
        {
            throw new InvalidOperationException("AOT deserialization mismatch!");
        }

        Console.WriteLine("✅ Native AOT Serialization & Deserialization Verified Successfully!");
    }
}
