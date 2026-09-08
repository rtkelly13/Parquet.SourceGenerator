using System;
using System.Text;
using Parquet.SourceGenerator.Tools;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Covers the README headline-table rewrite performed by <c>BenchmarkSummaryGenerator</c>.
/// </summary>
/// <remarks>
/// This code path only executes inside the benchmark workflow's auto-opened PR, which branch
/// protection kept from ever landing until #189 — so the writer had no test coverage and shipped
/// a UTF-8 BOM on every README it rewrote (#195). These tests hold the two properties the
/// workflow depends on: only the marker-delimited region changes, and BOM-less files stay
/// BOM-less. <c>File</c> is qualified because <c>Parquet.File</c> shadows it from this namespace.
/// </remarks>
public sealed class BenchmarkSummaryGeneratorTests : IDisposable
{
    private const string StartMarker = "<!-- BENCHMARK_TABLE_START -->";
    private const string EndMarker = "<!-- BENCHMARK_TABLE_END -->";

    private readonly string _tempDir = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"benchmark-summary-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void HeadlineRewriteLeavesBomlessReadmesBomless()
    {
        string readme = WriteReadme();
        Program.UpdateReadmeFile(readme, "## Fresh Table");
        byte[] bytes = System.IO.File.ReadAllBytes(readme);
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "README rewrite emitted a UTF-8 BOM."
        );
    }

    [Fact]
    public void HeadlineRewriteReplacesOnlyTheMarkerDelimitedRegion()
    {
        string readme = WriteReadme();
        Program.UpdateReadmeFile(readme, "## Fresh Table\n\n| row |");

        string content = System.IO.File.ReadAllText(readme);
        Assert.StartsWith("Preamble line", content, StringComparison.Ordinal);
        Assert.EndsWith("Postamble line", content, StringComparison.Ordinal);
        Assert.Contains($"{StartMarker}\n## Fresh Table", content, StringComparison.Ordinal);
        Assert.DoesNotContain("stale table", content, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(content, StartMarker));
        Assert.Equal(1, CountOccurrences(content, EndMarker));
    }

    [Fact]
    public void HeadlineRewriteWithoutMarkerBlockLeavesFileUntouched()
    {
        string readme = WriteReadme(withMarkers: false);
        string before = System.IO.File.ReadAllText(readme);
        Program.UpdateReadmeFile(readme, "## Fresh Table");
        Assert.Equal(before, System.IO.File.ReadAllText(readme));
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_tempDir))
        {
            System.IO.Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteReadme(bool withMarkers = true)
    {
        System.IO.Directory.CreateDirectory(_tempDir);
        string path = System.IO.Path.Combine(_tempDir, $"README-{Guid.NewGuid():N}.md");
        string content = withMarkers
            ? $"Preamble line\n\n{StartMarker}\nstale table\n{EndMarker}\n\nPostamble line"
            : "Preamble line\n\nPostamble line";
        System.IO.File.WriteAllText(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        return path;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
