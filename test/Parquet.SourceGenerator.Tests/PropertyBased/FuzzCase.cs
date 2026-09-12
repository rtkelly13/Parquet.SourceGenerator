using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Parquet.SourceGenerator.Tests.PropertyBased;

/// <summary>
/// One generated test case. A case is fully described by the fields below, and every one of them is
/// derived from the seed unless a shrink step has narrowed it, so printing a case is enough to
/// replay it exactly.
/// </summary>
public sealed record FuzzCase
{
    /// <summary>Gets the seed the case was derived from.</summary>
    public required long Seed { get; init; }

    /// <summary>Gets the number of rows written.</summary>
    public required int RowCount { get; init; }

    /// <summary>
    /// Gets the columns carrying fuzzed values. Columns outside this set keep their CLR default,
    /// which is what makes column-dropping a valid shrink step: the file schema never changes, only
    /// the interesting-ness of a column does.
    /// </summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Gets the compression method for the write.</summary>
    public required ParquetCompressionMethod Compression { get; init; }

    /// <summary>Gets the row group size, which drives the row-group layout of the file.</summary>
    public required int RowGroupSize { get; init; }

    /// <summary>Gets a value indicating whether the reader deduplicates strings.</summary>
    public required bool DeduplicateStrings { get; init; }

    /// <summary>Gets the runtime per-column encoding hints applied to the write.</summary>
    public required IReadOnlyDictionary<string, ParquetColumnEncoding> EncodingHints { get; init; }

    /// <summary>
    /// Derives a case from a seed. Every knob is a pure function of the seed, so the same seed
    /// yields the same case on any machine and any runtime.
    /// </summary>
    public static FuzzCase FromSeed(long seed)
    {
        var rng = new FuzzRandom(seed);
        int rowCount = rng.Pick([1, 2, 3, 7, 17, 64, 129, 257]);
        int columnCount = rng.Next(1, FuzzColumns.All.Count + 1);

        var names = FuzzColumns.All.Select(c => c.Name).ToList();
        rng.Shuffle(names);
        List<string> chosen = names.Take(columnCount).ToList();

        // Keep schema order so the printed case reads the same way the file is laid out.
        var chosenSet = new HashSet<string>(chosen, StringComparer.Ordinal);
        List<string> ordered = FuzzColumns
            .All.Where(c => chosenSet.Contains(c.Name))
            .Select(c => c.Name)
            .ToList();

        ParquetCompressionMethod compression = rng.Pick([
            ParquetCompressionMethod.None,
            ParquetCompressionMethod.Snappy,
            ParquetCompressionMethod.Gzip,
            ParquetCompressionMethod.Brotli,
            ParquetCompressionMethod.Zstd,
            ParquetCompressionMethod.Lz4,
        ]);

        int rowGroupSize = rng.Pick([1, 2, 5, 16, 64, 1_000, 50_000]);

        var hints = new Dictionary<string, ParquetColumnEncoding>(StringComparer.Ordinal);
        if (rng.Chance(0.5))
        {
            AddHint(hints, rng, ["text", "opt_text", "blob"], ParquetColumnEncoding.Dictionary);
        }

        if (rng.Chance(0.5))
        {
            AddHint(
                hints,
                rng,
                ["row_index", "i32", "i64", "opt_i64"],
                ParquetColumnEncoding.DeltaBinaryPacked
            );
        }

        if (rng.Chance(0.5))
        {
            AddHint(hints, rng, ["f32", "f64"], ParquetColumnEncoding.ByteSplitStream);
        }

        return new FuzzCase
        {
            Seed = seed,
            RowCount = rowCount,
            Columns = ordered,
            Compression = compression,
            RowGroupSize = rowGroupSize,
            DeduplicateStrings = rng.Chance(0.5),
            EncodingHints = hints,
        };
    }

    private static void AddHint(
        Dictionary<string, ParquetColumnEncoding> hints,
        FuzzRandom rng,
        string[] candidates,
        ParquetColumnEncoding encoding
    ) => hints[rng.Pick(candidates)] = encoding;

    /// <summary>Gets the fuzzed columns as column descriptors, in schema order.</summary>
    public IReadOnlyList<FuzzColumn> FuzzedColumns =>
        Columns.Select(name => FuzzColumns.ByName[name]).ToList();

    /// <summary>
    /// Builds the rows for this case. Each column draws from its own seeded stream, so shrinking the
    /// row count truncates every column's values without perturbing them, and dropping a column
    /// leaves the remaining columns byte-for-byte identical.
    /// </summary>
    public IReadOnlyList<FuzzWideRecord> BuildRows()
    {
        var rows = new List<FuzzWideRecord>(RowCount);
        for (int i = 0; i < RowCount; i++)
        {
            rows.Add(new FuzzWideRecord { RowIndex = i });
        }

        foreach (FuzzColumn column in FuzzedColumns)
        {
            var rng = new FuzzRandom(Seed ^ StableHash(column.Name));
            FuzzValueProfile profile = PickProfile(rng, column);
            double nullRate = profile switch
            {
                FuzzValueProfile.AllNull => 1.0,
                FuzzValueProfile.Sparse => 0.5,
                FuzzValueProfile.Constant => 0.0,
                _ => 0.1,
            };

            for (int i = 0; i < RowCount; i++)
            {
                object? value =
                    column.Optional && rng.Chance(nullRate)
                        ? null
                        : column.Generate(rng, profile, i);
                column.Assign(rows[i], value);
            }
        }

        return rows;
    }

    private static FuzzValueProfile PickProfile(FuzzRandom rng, FuzzColumn column) =>
        column.Optional
            ? rng.Pick([
                FuzzValueProfile.Typical,
                FuzzValueProfile.Boundary,
                FuzzValueProfile.Extreme,
                FuzzValueProfile.Sparse,
                FuzzValueProfile.AllNull,
                FuzzValueProfile.Constant,
            ])
            : rng.Pick([
                FuzzValueProfile.Typical,
                FuzzValueProfile.Boundary,
                FuzzValueProfile.Extreme,
                FuzzValueProfile.Constant,
            ]);

    /// <summary>
    /// FNV-1a. Any stable hash would do; <see cref="string.GetHashCode()"/> would not, because it is
    /// randomised per process and would make a "reproducible" seed reproduce nothing.
    /// </summary>
    private static long StableHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return (long)hash;
        }
    }

    /// <summary>Renders the case as the text a failure message prints.</summary>
    public string Describe() =>
        $"seed={Seed} rows={RowCount} rowGroupSize={RowGroupSize} compression={Compression} "
        + $"dedupeStrings={DeduplicateStrings} hints=[{string.Join(", ", EncodingHints.Select(kv => $"{kv.Key}:{kv.Value}"))}] "
        + $"columns=[{string.Join(", ", Columns)}]";

    /// <summary>Renders the shell command that replays exactly this case.</summary>
    public string ReproCommand() =>
        $"PARQUET_FUZZ_SEED={Seed.ToString(CultureInfo.InvariantCulture)} PARQUET_FUZZ_CASES=1 "
        + "dotnet test Parquet.SourceGenerator.slnx -c Release --filter FullyQualifiedName~PropertyBased";

    /// <summary>Serialises the case to the on-disk regression fixture format.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(FuzzCaseDocument.From(this), FuzzCaseDocument.Options);

    /// <summary>Reads a case back from the on-disk regression fixture format.</summary>
    public static FuzzCase FromJson(string json) =>
        (
            JsonSerializer.Deserialize<FuzzCaseDocument>(json, FuzzCaseDocument.Options)
            ?? throw new InvalidOperationException("Empty fuzz fixture.")
        ).ToCase();
}

/// <summary>
/// The on-disk shape of a regression fixture. Kept separate from <see cref="FuzzCase"/> so the
/// fixture format is an explicit, reviewable contract rather than whatever the record happens to
/// look like today.
/// </summary>
public sealed class FuzzCaseDocument
{
    public static JsonSerializerOptions Options { get; } =
        new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

    /// <summary>Gets or sets a human-written note about what this fixture pins down.</summary>
    public string Note { get; set; } = string.Empty;

    public long Seed { get; set; }

    public int RowCount { get; set; }

    public List<string> Columns { get; set; } = [];

    public ParquetCompressionMethod Compression { get; set; }

    public int RowGroupSize { get; set; }

    public bool DeduplicateStrings { get; set; }

    public Dictionary<string, ParquetColumnEncoding> EncodingHints { get; set; } = [];

    public static FuzzCaseDocument From(FuzzCase fuzzCase) =>
        new()
        {
            Seed = fuzzCase.Seed,
            RowCount = fuzzCase.RowCount,
            Columns = fuzzCase.Columns.ToList(),
            Compression = fuzzCase.Compression,
            RowGroupSize = fuzzCase.RowGroupSize,
            DeduplicateStrings = fuzzCase.DeduplicateStrings,
            EncodingHints = new Dictionary<string, ParquetColumnEncoding>(
                fuzzCase.EncodingHints,
                StringComparer.Ordinal
            ),
        };

    public FuzzCase ToCase() =>
        new()
        {
            Seed = Seed,
            RowCount = RowCount,
            Columns = Columns,
            Compression = Compression,
            RowGroupSize = RowGroupSize,
            DeduplicateStrings = DeduplicateStrings,
            EncodingHints = EncodingHints,
        };
}
