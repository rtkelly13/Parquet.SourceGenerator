using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CrossVersionInterop;

namespace PackageConsumptionLegacy;

/// <summary>
/// The classic (Parquet.Net 4.25) half of the cross-version interoperability leg of the
/// compatibility matrix. Pairs with the identically named driver in test/PackageConsumption: one
/// writes the interop file, the other reads it, and CI runs both directions.
/// </summary>
internal static class CrossVersionInteropDriver
{
    public const string Backend = "classic/Parquet.Net-4.25";

    public static List<InteropRow> CanonicalRows { get; } =
        new List<InteropRow>
        {
            new InteropRow
            {
                Id = 1,
                Name = "alpha",
                Value = 1.25,
                Ticks = 1_700_000_000_000,
            },
            new InteropRow
            {
                Id = 2,
                Name = null,
                Value = null,
                Ticks = 1_700_000_060_000,
            },
            new InteropRow
            {
                Id = 3,
                Name = "",
                Value = -0.5,
                Ticks = 1_700_000_120_000,
            },
        };

    public static async Task<int> WriteAsync(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var stream = File.Create(path))
        {
            await CanonicalRows.WriteParquetAsync(stream);
        }

        Console.WriteLine(
            $"{Backend} wrote {CanonicalRows.Count} interop rows to {path} ({new FileInfo(path).Length} bytes)."
        );
        return 0;
    }

    public static async Task<int> ReadAsync(string path, string producerBackend, string? matrixPath)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"FAILED: interop file not found: {path}");
            return 1;
        }

        using var stream = File.OpenRead(path);
        List<InteropRowEvolved> read =
            await InteropRowEvolvedParquetLegacyExtensions.ReadParquetAsync(stream);

        string? failure = InteropVerification.Verify(read, CanonicalRows);
        if (failure != null)
        {
            Console.Error.WriteLine($"FAILED: {failure}");
            return 1;
        }

        Console.WriteLine(
            $"{Backend} read {read.Count} rows written by {producerBackend}: columns resolved by name, "
                + "added_note/added_count absent from the file and materialised as null."
        );

        MatrixReport.Append(
            matrixPath,
            producer: producerBackend,
            consumer: Backend,
            schemaCase: "column-reordering+missing-optional-columns",
            outcome: "CompatibleWithNulls",
            detail: $"{read.Count} rows"
        );
        return 0;
    }
}
