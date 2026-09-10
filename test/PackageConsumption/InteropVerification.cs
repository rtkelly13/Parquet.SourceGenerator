using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CrossVersionInterop;

namespace PackageConsumption;

/// <summary>
/// Shared verification and matrix-row emission for the cross-version interoperability run. Kept
/// free of anything Parquet-specific so the legacy package project can compile the identical logic
/// on net472.
/// </summary>
internal static class InteropVerification
{
    /// <summary>Returns null when the read matches the canonical rows, or a failure message.</summary>
    public static string? Verify(
        IReadOnlyList<InteropRowEvolved> actual,
        IReadOnlyCollection<InteropRow> expected
    )
    {
        var expectedRows = new List<InteropRow>(expected);
        if (actual.Count != expectedRows.Count)
        {
            return $"expected {expectedRows.Count} interop rows, read {actual.Count}.";
        }

        for (int i = 0; i < expectedRows.Count; i++)
        {
            if (actual[i].Id != expectedRows[i].Id)
            {
                return $"row {i}: id {actual[i].Id}, expected {expectedRows[i].Id} (name-based resolution failed).";
            }

            if (actual[i].Ticks != expectedRows[i].Ticks)
            {
                return $"row {i}: ticks {actual[i].Ticks}, expected {expectedRows[i].Ticks}.";
            }

            if (!string.Equals(actual[i].Name, expectedRows[i].Name, StringComparison.Ordinal))
            {
                return $"row {i}: name '{actual[i].Name}', expected '{expectedRows[i].Name}'.";
            }

            if (actual[i].Value.HasValue != expectedRows[i].Value.HasValue)
            {
                return $"row {i}: value nullability differs.";
            }

            if (
                actual[i].Value.HasValue
                && Math.Abs(actual[i].Value!.Value - expectedRows[i].Value!.Value) > 1e-9
            )
            {
                return $"row {i}: value {actual[i].Value}, expected {expectedRows[i].Value}.";
            }

            if (actual[i].AddedNote is not null || actual[i].AddedCount is not null)
            {
                return $"row {i}: optional columns absent from the file should read as null, got "
                    + $"'{actual[i].AddedNote}'/{actual[i].AddedCount}.";
            }
        }

        return null;
    }
}

/// <summary>
/// Appends one compatibility-matrix row as a JSON line. JSON is hand-written rather than serialized
/// because this file also compiles for net472 in the legacy consumption project.
/// </summary>
internal static class MatrixReport
{
    public static void Append(
        string? path,
        string producer,
        string consumer,
        string schemaCase,
        string outcome,
        string detail
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string line = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"producer\":\"{0}\",\"consumer\":\"{1}\",\"schemaCase\":\"{2}\",\"outcome\":\"{3}\",\"detail\":\"{4}\"}}",
            Escape(producer),
            Escape(consumer),
            Escape(schemaCase),
            Escape(outcome),
            Escape(detail)
        );

        File.AppendAllText(path, line + Environment.NewLine);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
