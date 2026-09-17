using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parquet.SourceGenerator.ApiGates;
using Shouldly;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Enforces the declared-subset policy from issue #246: the classic backend remains a stable core
/// compatibility surface while modern-only features are allowed to grow in the v6 backend.
/// </summary>
public sealed class BackendCompatibilityPolicyTests
{
    private static readonly string GoldenFilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..",
        "..",
        "..",
        "GoldenFiles"
    );

    private static readonly HashSet<string> ClassicCoreMembers = new(StringComparer.Ordinal)
    {
        "Schema",
        "ReadParquetAsync",
        "ReadParquetArrayAsync",
        "WriteParquetAsync",
        "WriteParquetBatchedAsync",
        "WriteRowGroupAsync",
    };

    [Fact]
    public void ClassicBaselineContainsOnlyTheDeclaredCoreSurface()
    {
        string path = Directory.GetFiles(GoldenFilesDir, "*LegacyExtensions.api.txt").Single();
        string[] members = ReadMembers(path);

        members
            .Select(ApiCatalogue.SimpleName)
            .Distinct(StringComparer.Ordinal)
            .Except(ClassicCoreMembers, StringComparer.Ordinal)
            .ShouldBeEmpty(
                "The classic backend is a declared core subset. A new member requires an explicit "
                    + "compatibility-policy decision in docs/14-COMPATIBILITY-MATRIX.md."
            );

        foreach (string required in ClassicCoreMembers)
        {
            members.Select(ApiCatalogue.SimpleName).ShouldContain(required);
        }
    }

    [Fact]
    public void ModernBaselinesRetainTheModernOnlyCapabilities()
    {
        string[] lines = Directory
            .GetFiles(GoldenFilesDir, "*.api.txt")
            .Where(path => !path.EndsWith("LegacyExtensions.api.txt", StringComparison.Ordinal))
            .SelectMany(ReadMembers)
            .ToArray();

        lines.ShouldContain(line => line.Contains("Where(", StringComparison.Ordinal));
        lines.ShouldContain(line => line.Contains("Parallel()", StringComparison.Ordinal));
        lines.ShouldContain(line => line.Contains("ColumnBatch", StringComparison.Ordinal));
        lines.ShouldContain(line =>
            line.Contains("ReadParquetStreamAsync", StringComparison.Ordinal)
        );
    }

    private static string[] ReadMembers(string path) =>
        global::System
            .IO.File.ReadAllLines(path)
            .Where(line =>
                line.Length > 0
                && !line.StartsWith('#')
                && !line.StartsWith("//", StringComparison.Ordinal)
                && line.Contains(" -> ", StringComparison.Ordinal)
            )
            .ToArray();
}
