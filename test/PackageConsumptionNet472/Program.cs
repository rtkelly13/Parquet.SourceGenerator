// Validates that the Parquet.SourceGenerator NuGet package can be installed and compiled on
// .NET Framework 4.7.2. This is a compile-only validation: it proves that:
//
//   1. The generator package installs on net472 (transitive Attributes dependency resolves via
//      the netstandard2.0 TFM).
//   2. [ParquetSerializable] is found and the source generator emits code that compiles.
//
// It does NOT exercise the full read/write round-trip because Parquet.Net itself does not
// support net472. That coverage is provided by PackageConsumption on net8.0 and net9.0.

using System;
using Parquet.SourceGenerator;

namespace PackageConsumptionNet472
{
    [ParquetSerializable]
    public sealed partial class SensorReading
    {
        [ParquetColumn("sensor_id", Order = 1)]
        public string SensorId { get; set; } = string.Empty;

        [ParquetColumn("celsius", Order = 2)]
        public double Celsius { get; set; }

        [ParquetColumn("is_calibrated", Order = 3)]
        public bool IsCalibrated { get; set; }
    }

    internal static class Program
    {
        private static int Main()
        {
            // If the source generator ran, SensorReadingParquetExtensions exists and Schema is
            // accessible. This is a compile-time guarantee; at runtime we just sanity-check it.
            var schema = SensorReadingParquetExtensions.Schema;
            if (schema == null || schema.Fields.Count != 3)
            {
                Console.Error.WriteLine(
                    $"FAILED: expected 3 schema fields, got {schema?.Fields.Count.ToString() ?? "null"}.");
                return 1;
            }

            Console.WriteLine(
                $"Package consumption OK (net472): generator compiled, schema has {schema.Fields.Count} fields.");
            return 0;
        }
    }
}
