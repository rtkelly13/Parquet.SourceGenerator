// Verifies the produced NuGet packages actually work when consumed, which building them does not.
//
// Everything here is written from the outside in: the only references are the Parquet.SourceGenerator
// package and Parquet.Net. If the generator ships wrongly — no analyzer in analyzers/dotnet/cs, a
// missing dependency on the Attributes package, or the generator assembly leaking into lib/ — this
// file stops compiling or stops running, and CI fails at the step after `dotnet pack`.

using Parquet.SourceGenerator;

namespace PackageConsumption;

[ParquetSerializable]
public sealed partial record Reading
{
    [ParquetColumn("sensor_id", Order = 1)]
    public string SensorId { get; init; } = string.Empty;

    [ParquetColumn("celsius", Order = 2)]
    public double Celsius { get; init; }

    [ParquetColumn("captured_at_ms", Order = 3)]
    public long CapturedAtMs { get; init; }

    [ParquetColumn("is_calibrated", Order = 4)]
    public bool IsCalibrated { get; init; }

    [ParquetIgnore]
    public string Scratch { get; init; } = "not persisted";
}

internal static class Program
{
    private static async Task<int> Main()
    {
        var expected = new List<Reading>
        {
            new() { SensorId = "sensor-a", Celsius = 21.5, CapturedAtMs = 1_700_000_000_000, IsCalibrated = true },
            new() { SensorId = "sensor-b", Celsius = -4.25, CapturedAtMs = 1_700_000_060_000, IsCalibrated = false },
            new() { SensorId = "sensor-c", Celsius = 0.0, CapturedAtMs = 1_700_000_120_000, IsCalibrated = true },
        };

        using var stream = new MemoryStream();
        await expected.WriteParquetAsync(stream);

        // A generated schema that produced no bytes would still "round-trip" an empty list, so
        // assert the stream was actually written to before reading it back.
        if (stream.Length == 0)
        {
            return Fail("WriteParquetAsync produced an empty stream.");
        }

        stream.Position = 0;
        List<Reading> actual = await ReadingParquetExtensions.ReadParquetAsync(stream);

        if (actual.Count != expected.Count)
        {
            return Fail($"Expected {expected.Count} records, read {actual.Count}.");
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (actual[i].SensorId != expected[i].SensorId
                || actual[i].Celsius != expected[i].Celsius
                || actual[i].CapturedAtMs != expected[i].CapturedAtMs
                || actual[i].IsCalibrated != expected[i].IsCalibrated)
            {
                return Fail($"Record {i} did not round-trip: expected {expected[i]}, got {actual[i]}.");
            }
        }

        // [ParquetIgnore] members are not persisted, so they come back as the default rather than
        // the value that was written.
        if (actual[0].Scratch != "not persisted")
        {
            return Fail($"Expected [ParquetIgnore] member to be left at its initializer, got '{actual[0].Scratch}'.");
        }

        // The static schema is part of the generated public surface; a consumer can reach it.
        if (ReadingParquetExtensions.Schema.Fields.Count != 4)
        {
            return Fail($"Expected 4 schema fields, found {ReadingParquetExtensions.Schema.Fields.Count}.");
        }

        Console.WriteLine($"Package consumption OK: round-tripped {actual.Count} records, schema has "
                          + $"{ReadingParquetExtensions.Schema.Fields.Count} fields.");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"Package consumption FAILED: {message}");
        return 1;
    }
}
