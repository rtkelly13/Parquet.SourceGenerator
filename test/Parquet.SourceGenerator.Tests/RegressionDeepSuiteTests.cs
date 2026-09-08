using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record DeepSuiteModel
{
    [ParquetColumn("id")]
    public long Id { get; init; }

    [ParquetColumn("name")]
    public string? Name { get; init; }

    [ParquetColumn("score")]
    public double Score { get; init; }

    [ParquetColumn("flag")]
    public bool? Flag { get; init; }

    [ParquetColumn("payload")]
    public byte[]? Payload { get; init; }
}

/// <summary>
/// The deep tier of the <c>/regression</c> suite: seeded property coverage, corrupted inputs and
/// large multi-row-group datasets.
/// </summary>
/// <remarks>
/// These are trait-gated so that neither the default <c>dotnet test</c> loop nor the required PR
/// checks pay for them — <see cref="Parquet.SourceGenerator.Tools.Regression.RegressionPlan.DefaultTestFilter"/>
/// excludes every category used here. Scale is read from the environment: the runner's deep mode
/// sets <c>PARQUET_REGRESSION_PROPERTY_SEEDS</c> and <c>PARQUET_REGRESSION_LARGE_ROWS</c>, and the
/// defaults below keep a plain <c>dotnet test</c> honest but quick.
///
/// This is a starter corpus, not the full one. Issue #169 owns the broad supported-schema property
/// matrix and the generated corrupted-file corpus; the tiers, traits and environment contract here
/// are what that work plugs into.
/// </remarks>
public sealed class RegressionDeepSuiteTests
{
    private static int PropertySeeds => ReadScale("PARQUET_REGRESSION_PROPERTY_SEEDS", 24, 4096);

    private static int LargeRowCount =>
        ReadScale("PARQUET_REGRESSION_LARGE_ROWS", 25_000, 2_000_000);

    [Fact]
    [Trait("Category", "Property")]
    public async Task SeededRandomRecordsSurviveRoundTrip()
    {
        int seeds = PropertySeeds;
        for (int seed = 0; seed < seeds; seed++)
        {
            var random = new Random(seed);
            List<DeepSuiteModel> rows = Enumerable
                .Range(0, 1 + random.Next(40))
                .Select(_ => NextRow(random))
                .ToList();

            using var stream = new MemoryStream();
            await rows.WriteParquetBatchedAsync(stream, rowGroupSize: 1 + random.Next(16));
            stream.Position = 0;

            List<DeepSuiteModel> read = await DeepSuiteModelParquetExtensions.ReadParquetAsync(
                stream
            );

            // The seed is in every failure message on purpose: a property failure is worthless if
            // it cannot be replayed, and `--seeds` plus this number reproduces the exact case.
            Assert.True(
                rows.Count == read.Count,
                $"seed {seed.ToString(CultureInfo.InvariantCulture)}: wrote {rows.Count} rows, read {read.Count}"
            );

            for (int i = 0; i < rows.Count; i++)
            {
                Assert.True(
                    rows[i].Id == read[i].Id
                        && string.Equals(rows[i].Name, read[i].Name, StringComparison.Ordinal)
                        && rows[i].Score.Equals(read[i].Score)
                        && rows[i].Flag == read[i].Flag
                        && BytesEqual(rows[i].Payload, read[i].Payload),
                    $"seed {seed.ToString(CultureInfo.InvariantCulture)} row {i.ToString(CultureInfo.InvariantCulture)} did not round-trip"
                );
            }
        }
    }

    [Theory]
    [Trait("Category", "Corruption")]
    [InlineData("truncated-header", true)]
    [InlineData("truncated-footer", true)]
    [InlineData("garbage", false)]
    [InlineData("empty", true)]
    [InlineData("footer-length-lie", true)]
    public async Task CorruptedInputFailsCleanly(string corruption, bool mustThrow)
    {
        byte[] valid = await WriteSampleAsync(64);
        byte[] corrupted = Corrupt(valid, corruption);

        using var stream = new MemoryStream(corrupted, writable: false);

        // "Cleanly" means a catchable exception, or a terminating read. What is not acceptable is
        // an unrecoverable failure. Structural damage (no header, no footer, an impossible footer
        // length) must be detected and reported; random byte flips may or may not be detectable
        // depending on where they land, so that case only has to stay recoverable.
        List<DeepSuiteModel>? rows = null;
        Exception? thrown = await Record.ExceptionAsync(async () =>
            rows = await DeepSuiteModelParquetExtensions.ReadParquetAsync(stream)
        );

        if (thrown is not null)
        {
            Assert.False(
                thrown is OutOfMemoryException or StackOverflowException,
                $"{corruption} produced an unrecoverable failure: {thrown.GetType().FullName}: {thrown.Message}"
            );
            return;
        }

        Assert.False(
            mustThrow,
            $"{corruption} was accepted silently and returned {(rows?.Count ?? 0).ToString(CultureInfo.InvariantCulture)} rows; structural corruption must be reported."
        );
    }

    [Fact]
    [Trait("Category", "Corruption")]
    public async Task CorruptionCoverageNeverTouchesCheckedInFixtures()
    {
        // Corruption tests work on bytes produced in-memory. If one ever starts from a checked-in
        // fixture it must copy first; this test states the rule so the copy is not "optimised" away.
        string fixtureRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data")
        );
        Assert.True(Directory.Exists(fixtureRoot), $"fixture root missing: {fixtureRoot}");

        byte[] valid = await WriteSampleAsync(8);
        byte[] corrupted = Corrupt(valid, "garbage");
        Assert.NotEqual(valid, corrupted);

        foreach (
            string file in Directory.EnumerateFiles(
                fixtureRoot,
                "*.parquet",
                SearchOption.AllDirectories
            )
        )
        {
            var info = new FileInfo(file);
            Assert.True(info.Length > 0, $"fixture emptied: {file}");
        }
    }

    [Fact]
    [Trait("Category", "LargeDataset")]
    public async Task LargeMultiRowGroupDatasetRoundTrips()
    {
        int rowCount = LargeRowCount;
        List<DeepSuiteModel> rows = Enumerable
            .Range(0, rowCount)
            .Select(i => new DeepSuiteModel
            {
                Id = i,
                Name = (i % 7 == 0) ? null : "row-" + i.ToString(CultureInfo.InvariantCulture),
                Score = i * 0.5,
                Flag = (i % 3 == 0) ? null : (i % 2 == 0),
                Payload = (i % 11 == 0) ? null : new byte[] { (byte)i, (byte)(i >> 8) },
            })
            .ToList();

        using var stream = new MemoryStream();
        await rows.WriteParquetBatchedAsync(stream, rowGroupSize: 5_000);
        stream.Position = 0;

        List<DeepSuiteModel> read = await DeepSuiteModelParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(rowCount, read.Count);
        Assert.Equal(rows[0].Id, read[0].Id);
        Assert.Equal(rows[^1].Id, read[^1].Id);
        Assert.Null(read[0].Name);
        Assert.Equal(rows.Sum(row => row.Score), read.Sum(row => row.Score));
    }

    private static DeepSuiteModel NextRow(Random random) =>
        new()
        {
            Id = random.NextInt64(long.MinValue / 2, long.MaxValue / 2),
            Name = random.Next(4) == 0 ? null : RandomString(random),
            Score = random.Next(3) switch
            {
                0 => 0d,
                1 => random.NextDouble() * 1e9,
                _ => -random.NextDouble(),
            },
            Flag = random.Next(3) switch
            {
                0 => null,
                1 => true,
                _ => false,
            },
            Payload = random.Next(3) == 0 ? null : RandomBytes(random),
        };

    private static string RandomString(Random random)
    {
        int length = random.Next(0, 24);
        char[] characters = new char[length];
        for (int i = 0; i < length; i++)
        {
            characters[i] = (char)random.Next('a', 'z' + 1);
        }

        return new string(characters);
    }

    private static byte[] RandomBytes(Random random)
    {
        byte[] bytes = new byte[random.Next(0, 32)];
        random.NextBytes(bytes);
        return bytes;
    }

    private static bool BytesEqual(byte[]? left, byte[]? right) =>
        (left is null && right is null)
        || (left is not null && right is not null && left.AsSpan().SequenceEqual(right));

    private static async Task<byte[]> WriteSampleAsync(int rowCount)
    {
        List<DeepSuiteModel> rows = Enumerable
            .Range(0, rowCount)
            .Select(i => new DeepSuiteModel
            {
                Id = i,
                Name = "row-" + i.ToString(CultureInfo.InvariantCulture),
                Score = i,
                Flag = i % 2 == 0,
                Payload = new byte[] { (byte)i },
            })
            .ToList();

        using var stream = new MemoryStream();
        await rows.WriteParquetBatchedAsync(stream, rowGroupSize: 16);
        return stream.ToArray();
    }

    private static byte[] Corrupt(byte[] valid, string corruption)
    {
        switch (corruption)
        {
            case "empty":
                return Array.Empty<byte>();
            case "garbage":
                byte[] garbage = valid.ToArray();
                var random = new Random(1234);
                for (int i = 0; i < garbage.Length; i += 7)
                {
                    garbage[i] = (byte)random.Next(256);
                }

                return garbage;
            case "truncated-header":
                return valid.Skip(8).ToArray();
            case "truncated-footer":
                return valid.Take(valid.Length / 2).ToArray();
            case "footer-length-lie":
                byte[] lie = valid.ToArray();
                // The four bytes before the trailing PAR1 magic are the footer length. Making it
                // absurd is the classic way a truncated upload presents itself.
                if (lie.Length > 8)
                {
                    lie[^8] = 0xFF;
                    lie[^7] = 0xFF;
                    lie[^6] = 0xFF;
                    lie[^5] = 0x7F;
                }

                return lie;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
        }
    }

    private static int ReadScale(string variable, int fallback, int maximum)
    {
        string? raw = Environment.GetEnvironmentVariable(variable);
        if (
            !string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            && parsed > 0
        )
        {
            return Math.Min(parsed, maximum);
        }

        return fallback;
    }
}
