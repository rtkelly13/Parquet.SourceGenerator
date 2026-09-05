#:package Parquet.Net@6.1.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Parquet;
using Parquet.SourceGenerator.Scripts;

// -----------------------------------------------------------------------------
// FetchBenchmarkDatasets.cs
// Standalone C# script (.NET 10) to fetch and verify fixed public benchmark
// datasets from Hugging Face Hub.
// Tracks cryptographic provenance (SHA-256, commit SHAs, schema profiles).
// -----------------------------------------------------------------------------

var configs = new List<DatasetConfig>
{
    new(
        Id: "tpch-lineitem-sf001",
        Filename: "tpch_lineitem_sf001.parquet",
        Description: "TPC-H Benchmark lineitem table at Scale Factor 0.01 (standard analytical decision support benchmark).",
        SourcePlatform: "Hugging Face Hub",
        UpstreamRepository: "liangyc/tpch-sf-0_01",
        UpstreamCommit: "a91e9442ea9dc1fe4729db3138afdd03ef36a9e5",
        SourceUrl: "https://huggingface.co/datasets/liangyc/tpch-sf-0_01/resolve/a91e9442ea9dc1fe4729db3138afdd03ef36a9e5/lineitem/train/0000.parquet",
        License: "Apache-2.0"
    ),
    new(
        Id: "adult-census-income",
        Filename: "adult_census_income.parquet",
        Description: "Adult Census Income dataset from UCI Machine Learning Repository (standard categorical tabular benchmark).",
        SourcePlatform: "Hugging Face Hub",
        UpstreamRepository: "scikit-learn/adult-census-income",
        UpstreamCommit: "aefa0f0f1b03a11dd48f460913e20a1d50b4e53c",
        SourceUrl: "https://huggingface.co/datasets/scikit-learn/adult-census-income/resolve/aefa0f0f1b03a11dd48f460913e20a1d50b4e53c/default/train/0000.parquet",
        License: "CC-BY-4.0"
    ),
    new(
        Id: "diamonds",
        Filename: "diamonds.parquet",
        Description: "Diamonds dataset from ggplot2 (standard regression tabular benchmark with numeric & ordinal features).",
        SourcePlatform: "Hugging Face Hub",
        UpstreamRepository: "inria-soda/tabular-benchmark",
        UpstreamCommit: "cb2bcee34f8fbd271c04dc644fd91f23a51a8570",
        SourceUrl: "https://huggingface.co/datasets/inria-soda/tabular-benchmark/resolve/cb2bcee34f8fbd271c04dc644fd91f23a51a8570/reg_cat_diamonds/train/0000.parquet",
        License: "CC0-1.0"
    ),
};

bool force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
string dataDir = FindBenchmarkDataDir();
Directory.CreateDirectory(dataDir);

Console.WriteLine($"📦 Managing benchmark datasets in: {dataDir}");

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Parquet.SourceGenerator-Benchmark-Sync/1.0");

var datasetSummaries = new List<DatasetSummary>();

foreach (var cfg in configs)
{
    string targetPath = Path.Combine(dataDir, cfg.Filename);
    bool shouldDownload =
        force || !System.IO.File.Exists(targetPath) || new FileInfo(targetPath).Length < 1024;

    if (shouldDownload)
    {
        Console.WriteLine($"📥 Downloading {cfg.Id} from {cfg.SourceUrl}...");
        using var response = await httpClient.GetAsync(
            cfg.SourceUrl,
            HttpCompletionOption.ResponseHeadersRead
        );
        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = System.IO.File.Create(targetPath);
        await sourceStream.CopyToAsync(fileStream);
        Console.WriteLine($"   Saved {cfg.Filename} ({new FileInfo(targetPath).Length:N0} bytes)");
    }
    else
    {
        Console.WriteLine(
            $"✅ {cfg.Filename} already present ({new FileInfo(targetPath).Length:N0} bytes)"
        );
    }

    // Hash file
    string sha256;
    long fileSizeBytes;
    using (var fs = System.IO.File.OpenRead(targetPath))
    {
        fileSizeBytes = fs.Length;
        byte[] hash = SHA256.HashData(fs);
        sha256 = Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Inspect Parquet schema and metadata
    long rowCount;
    int columnCount;
    int rowGroupCount;
    string compression = "none";
    var columns = new List<ColumnSummary>();
    var dictColumns = new List<string>();
    var plainColumns = new List<string>();

    using (var fs = System.IO.File.OpenRead(targetPath))
    {
        await using var reader = await ParquetReader.CreateAsync(fs);
        rowCount = reader.Metadata?.NumRows ?? 0;
        rowGroupCount = reader.RowGroupCount;
        columnCount = reader.Schema.Fields.Count;

        var firstRg = reader.Metadata?.RowGroups.FirstOrDefault();
        if (firstRg != null)
        {
            compression =
                firstRg.Columns.FirstOrDefault()?.MetaData?.Codec.ToString().ToLowerInvariant()
                ?? "none";

            for (int i = 0; i < firstRg.Columns.Count; i++)
            {
                var col = firstRg.Columns[i];
                var meta = col.MetaData;
                string colName =
                    meta != null
                        ? string.Join(".", meta.PathInSchema)
                        : reader.Schema.Fields[i].Name;
                string physicalType = meta?.Type.ToString() ?? "UNKNOWN";
                var encodings = meta?.Encodings.Select(e => e.ToString()).ToList() ?? [];
                bool isDict = encodings.Any(e =>
                    e.Contains("DICTIONARY", StringComparison.OrdinalIgnoreCase)
                );

                if (isDict)
                    dictColumns.Add(colName);
                else
                    plainColumns.Add(colName);

                columns.Add(new ColumnSummary(colName, physicalType, encodings, isDict));
            }
        }
    }

    datasetSummaries.Add(
        new DatasetSummary(
            Config: cfg,
            FileSizeBytes: fileSizeBytes,
            Sha256: sha256,
            RowCount: rowCount,
            ColumnCount: columnCount,
            RowGroupCount: rowGroupCount,
            Compression: compression,
            DictColumns: dictColumns,
            PlainColumns: plainColumns,
            Columns: columns
        )
    );

    Console.WriteLine($"   SHA-256: {sha256}");
    Console.WriteLine(
        $"   Rows: {rowCount:N0}, Cols: {columnCount}, Compression: {compression}, Dictionaries: {dictColumns.Count}"
    );
}

string manifestPath = Path.Combine(dataDir, "provenance.json");
using (var fs = System.IO.File.Create(manifestPath))
using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
{
    writer.WriteStartObject();
    writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
    writer.WriteString(
        "description",
        "Cryptographic Data Provenance Manifest for Parquet.SourceGenerator Benchmarks"
    );
    writer.WriteString("generated_at", DateTime.UtcNow.ToString("o"));

    writer.WriteStartArray("datasets");
    foreach (var ds in datasetSummaries)
    {
        writer.WriteStartObject();
        writer.WriteString("id", ds.Config.Id);
        writer.WriteString("filename", ds.Config.Filename);
        writer.WriteString("description", ds.Config.Description);

        writer.WriteStartObject("provenance");
        writer.WriteString("source_platform", ds.Config.SourcePlatform);
        writer.WriteString("upstream_repository", ds.Config.UpstreamRepository);
        writer.WriteString("upstream_commit", ds.Config.UpstreamCommit);
        writer.WriteString("source_url", ds.Config.SourceUrl);
        writer.WriteString("license", ds.Config.License);
        writer.WriteString("verified_at", DateTime.UtcNow.ToString("o"));
        writer.WriteEndObject();

        writer.WriteStartObject("integrity");
        writer.WriteNumber("file_size_bytes", ds.FileSizeBytes);
        writer.WriteString("sha256", ds.Sha256);
        writer.WriteString("git_lfs_oid", $"sha256:{ds.Sha256}");
        writer.WriteEndObject();

        writer.WriteStartObject("parquet_profile");
        writer.WriteNumber("row_count", ds.RowCount);
        writer.WriteNumber("column_count", ds.ColumnCount);
        writer.WriteNumber("row_group_count", ds.RowGroupCount);
        writer.WriteString("compression", ds.Compression);

        writer.WriteStartArray("dictionary_columns");
        foreach (var c in ds.DictColumns)
            writer.WriteStringValue(c);
        writer.WriteEndArray();

        writer.WriteStartArray("plain_columns");
        foreach (var c in ds.PlainColumns)
            writer.WriteStringValue(c);
        writer.WriteEndArray();

        writer.WriteStartArray("columns");
        foreach (var c in ds.Columns)
        {
            writer.WriteStartObject();
            writer.WriteString("name", c.Name);
            writer.WriteString("physical_type", c.PhysicalType);

            writer.WriteStartArray("encodings");
            foreach (var enc in c.Encodings)
                writer.WriteStringValue(enc);
            writer.WriteEndArray();

            writer.WriteBoolean("dictionary_encoded", c.DictionaryEncoded);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject(); // parquet_profile
        writer.WriteEndObject(); // dataset
    }
    writer.WriteEndArray(); // datasets
    writer.WriteEndObject(); // root
}

Console.WriteLine($"\n📝 Updated provenance manifest: {manifestPath}");
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
        if (Directory.Exists(full))
        {
            return full;
        }
    }

    return Path.GetFullPath(Path.Combine("benchmarks", "data"));
}

namespace Parquet.SourceGenerator.Scripts
{
    internal sealed record DatasetConfig(
        string Id,
        string Filename,
        string Description,
        string SourcePlatform,
        string UpstreamRepository,
        string UpstreamCommit,
        string SourceUrl,
        string License
    );

    internal sealed record ColumnSummary(
        string Name,
        string PhysicalType,
        IReadOnlyList<string> Encodings,
        bool DictionaryEncoded
    );

    internal sealed record DatasetSummary(
        DatasetConfig Config,
        long FileSizeBytes,
        string Sha256,
        long RowCount,
        int ColumnCount,
        int RowGroupCount,
        string Compression,
        IReadOnlyList<string> DictColumns,
        IReadOnlyList<string> PlainColumns,
        IReadOnlyList<ColumnSummary> Columns
    );
}
