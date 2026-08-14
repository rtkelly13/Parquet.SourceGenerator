using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Parquet.SourceGenerator;

namespace PackageConsumptionLegacy;

[ParquetSerializable]
public sealed partial class Measurement
{
    [ParquetColumn("id", Order = 1)]
    public int Id { get; set; }

    [ParquetColumn("val", Order = 2)]
    public double Val { get; set; }
}

internal static class Program
{
    private static async Task<int> Main()
    {
        var expected = new List<Measurement>
        {
            new() { Id = 1, Val = 10.5 },
            new() { Id = 2, Val = 20.5 },
        };

        using var stream = new MemoryStream();
        await expected.WriteParquetAsync(stream);

        if (stream.Length == 0)
        {
            Console.Error.WriteLine("FAILED: WriteParquetAsync produced empty stream.");
            return 1;
        }

        stream.Position = 0;
        List<Measurement> actual = await MeasurementParquetLegacyExtensions.ReadParquetAsync(stream);

        if (actual.Count != expected.Count)
        {
            Console.Error.WriteLine($"FAILED: Expected {expected.Count} items, got {actual.Count}");
            return 1;
        }

        Console.WriteLine($"PackageConsumptionLegacy OK: round-tripped {actual.Count} records.");
        return 0;
    }
}
