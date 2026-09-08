using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IOFile = System.IO.File;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// How a producer/consumer/schema combination is expected to behave. The vocabulary is deliberately
/// small: a case either works, works by materialising nulls for columns the file does not carry, or
/// is rejected with an error a caller can act on. "Threw something" is not an outcome — an
/// unexpected exception type or an opaque message fails the case.
/// </summary>
public enum CompatibilityOutcome
{
    /// <summary>Every value the model declares came back from the file unchanged.</summary>
    Compatible,

    /// <summary>
    /// The file omits optional columns the model declares; those materialise as nulls and every
    /// column the file does carry comes back unchanged.
    /// </summary>
    CompatibleWithNulls,

    /// <summary>
    /// The combination is outside the envelope and the generated reader says so: a typed exception
    /// naming the column that could not be satisfied.
    /// </summary>
    RejectedWithClearError,
}

/// <summary>
/// One executed matrix cell. Producer and format versions are read out of the file's own footer
/// wherever a file is involved, so the recorded row reports what was actually exercised rather than
/// what a fixture manifest claims.
/// </summary>
public sealed record CompatibilityMatrixResult(
    string CaseId,
    string Producer,
    string ProducerVersion,
    string Consumer,
    string ConsumerVersion,
    string FormatVersion,
    string SchemaCase,
    CompatibilityOutcome Outcome,
    string Detail
);

/// <summary>
/// Collects executed matrix cells and writes them out as JSON and Markdown so a CI run leaves
/// behind a compatibility record rather than only a pass/fail.
/// </summary>
public static class CompatibilityMatrixRecorder
{
    private static readonly ConcurrentBag<CompatibilityMatrixResult> Results = new();

    /// <summary>
    /// The Parquet.Net version underneath the generated code, which is the consumer version for
    /// every in-process case and the producer version for files written during the run.
    /// </summary>
    public static string ParquetNetVersion { get; } = ResolveParquetNetVersion();

    private static string ResolveParquetNetVersion()
    {
        // Parquet.Net stamps its assembly version at 6.0.0.0 across the whole 6.x line, so the
        // assembly name is useless for a version matrix. The informational version carries the
        // package version.
        System.Reflection.Assembly assembly = typeof(global::Parquet.ParquetReader).Assembly;
        string? informational = assembly
            .GetCustomAttributes(
                typeof(System.Reflection.AssemblyInformationalVersionAttribute),
                inherit: false
            )
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;

        string version = informational ?? assembly.GetName().Version?.ToString(3) ?? "unknown";
        int plus = version.IndexOf('+');
        return plus >= 0 ? version.Substring(0, plus) : version;
    }

    public static void Record(CompatibilityMatrixResult result) => Results.Add(result);

    public static IReadOnlyList<CompatibilityMatrixResult> Snapshot() =>
        Results.OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Resolves the directory the report is written to. CI can redirect it with
    /// <c>PARQUET_COMPAT_MATRIX_OUTPUT</c>; otherwise it lands beside the test binaries.
    /// </summary>
    public static string ResolveOutputDirectory() =>
        Environment.GetEnvironmentVariable("PARQUET_COMPAT_MATRIX_OUTPUT") is string configured
        && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "compatibility-matrix");

    public static void WriteReport()
    {
        IReadOnlyList<CompatibilityMatrixResult> snapshot = Snapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        string directory = ResolveOutputDirectory();
        Directory.CreateDirectory(directory);

        IOFile.WriteAllText(
            Path.Combine(directory, "compatibility-matrix.json"),
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    generatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    parquetNetVersion = ParquetNetVersion,
                    results = snapshot,
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );

        var markdown = new StringBuilder();
        markdown.AppendLine("# Compatibility Matrix Results");
        markdown.AppendLine();
        markdown.AppendLine(
            CultureInfo.InvariantCulture,
            $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC against Parquet.Net {ParquetNetVersion}."
        );
        markdown.AppendLine();
        markdown.AppendLine(
            "| Case | Producer | Producer version | Format | Consumer | Consumer version | Schema case | Outcome |"
        );
        markdown.AppendLine("|:---|:---|:---|:---|:---|:---|:---|:---|");
        foreach (CompatibilityMatrixResult r in snapshot)
        {
            markdown.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {r.CaseId} | {r.Producer} | {r.ProducerVersion} | {r.FormatVersion} | {r.Consumer} | {r.ConsumerVersion} | {r.SchemaCase} | {r.Outcome} |"
            );
        }

        IOFile.WriteAllText(
            Path.Combine(directory, "compatibility-matrix.md"),
            markdown.ToString()
        );
    }
}

/// <summary>
/// Flushes the recorded matrix once the matrix test class has finished. A class fixture is used
/// rather than a process-exit hook so the report is written at a point the test host still owns.
/// </summary>
public sealed class CompatibilityMatrixReportFixture : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CompatibilityMatrixRecorder.WriteReport();
    }
}
