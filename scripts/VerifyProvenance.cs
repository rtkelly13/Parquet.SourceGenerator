#:package Parquet.Net@6.0.3

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Parquet;

// -----------------------------------------------------------------------------
// VerifyProvenance.cs
// Verifies that all Git LFS Parquet files under benchmarks/data/ are:
// 1. Properly hydrated (not raw LFS text pointers < 1024 bytes).
// 2. Cryptographically identical to the recorded provenance.json SHA-256 digest.
// 3. Structurally valid with expected row count and column count.
// -----------------------------------------------------------------------------

string dataDir = FindBenchmarkDataDir();
string manifestPath = Path.Combine(dataDir, "provenance.json");

if (!System.IO.File.Exists(manifestPath))
{
    Console.Error.WriteLine($"❌ Provenance manifest not found: {manifestPath}");
    return 1;
}

using var manifestDoc = JsonDocument.Parse(System.IO.File.ReadAllText(manifestPath));
var root = manifestDoc.RootElement;

if (!root.TryGetProperty("datasets", out var datasetsElem) || datasetsElem.GetArrayLength() == 0)
{
    Console.Error.WriteLine($"❌ No datasets found in manifest: {manifestPath}");
    return 1;
}

int datasetCount = datasetsElem.GetArrayLength();
Console.WriteLine(
    $"🔍 Verifying provenance and cryptographic integrity for {datasetCount} dataset(s)..."
);

var errors = new List<string>();

foreach (var ds in datasetsElem.EnumerateArray())
{
    string id = ds.GetProperty("id").GetString()!;
    string filename = ds.GetProperty("filename").GetString()!;
    string filePath = Path.Combine(dataDir, filename);

    if (!System.IO.File.Exists(filePath))
    {
        errors.Add($"Missing file: {filePath}");
        continue;
    }

    var fileInfo = new FileInfo(filePath);
    if (fileInfo.Length < 1024)
    {
        errors.Add(
            $"{filename} is only {fileInfo.Length} bytes! Likely an unhydrated Git LFS pointer. "
                + $"Run 'git lfs pull' or verify 'lfs: true' in actions/checkout."
        );
        continue;
    }

    // Compute SHA-256
    string actualSha256;
    using (var fs = System.IO.File.OpenRead(filePath))
    {
        byte[] hashBytes = SHA256.HashData(fs);
        actualSha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    string expectedSha256 = ds.GetProperty("integrity").GetProperty("sha256").GetString()!;
    if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
    {
        errors.Add(
            $"SHA-256 mismatch for {filename}: expected {expectedSha256}, got {actualSha256}"
        );
        continue;
    }

    // Check structural Parquet validity
    long expectedRows = ds.GetProperty("parquet_profile").GetProperty("row_count").GetInt64();
    int expectedCols = ds.GetProperty("parquet_profile").GetProperty("column_count").GetInt32();

    try
    {
        using var fs = System.IO.File.OpenRead(filePath);
        await using var reader = await ParquetReader.CreateAsync(fs);

        long actualRows = reader.Metadata?.NumRows ?? 0;
        int actualCols = reader.Schema.Fields.Count;

        if (actualRows != expectedRows)
        {
            errors.Add(
                $"Row count mismatch for {filename}: expected {expectedRows:N0}, got {actualRows:N0}"
            );
            continue;
        }

        if (actualCols != expectedCols)
        {
            errors.Add(
                $"Column count mismatch for {filename}: expected {expectedCols}, got {actualCols}"
            );
            continue;
        }

        Console.WriteLine(
            $"  ✅ {id} ({filename}): Verified SHA-256 & {actualRows:N0} rows, {actualCols} columns"
        );
    }
    catch (Exception ex)
    {
        errors.Add($"Failed to inspect Parquet metadata for {filename}: {ex.Message}");
    }
}

if (errors.Count > 0)
{
    Console.Error.WriteLine("\n❌ Provenance verification failed with the following error(s):");
    foreach (var err in errors)
    {
        Console.Error.WriteLine($"  - {err}");
    }
    return 1;
}

Console.WriteLine(
    "\n🎉 All benchmark datasets passed provenance and cryptographic integrity checks!"
);
return 0;

static string FindBenchmarkDataDir()
{
    string[] candidates =
    [
        Path.Combine(Directory.GetCurrentDirectory(), "benchmarks", "data"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "benchmarks", "data"),
        Path.Combine(AppContext.BaseDirectory, "benchmarks", "data"),
        Path.Combine(AppContext.BaseDirectory, "data"),
    ];

    foreach (var candidate in candidates)
    {
        string full = Path.GetFullPath(candidate);
        if (Directory.Exists(full) && System.IO.File.Exists(Path.Combine(full, "provenance.json")))
        {
            return full;
        }
    }

    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        string candidate = Path.Combine(dir.FullName, "benchmarks", "data");
        if (
            Directory.Exists(candidate)
            && System.IO.File.Exists(Path.Combine(candidate, "provenance.json"))
        )
        {
            return Path.GetFullPath(candidate);
        }
        dir = dir.Parent;
    }

    return Path.GetFullPath(Path.Combine("benchmarks", "data"));
}
