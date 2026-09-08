using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IOFile = System.IO.File;

namespace Parquet.SourceGenerator.Tests.PropertyBased;

/// <summary>
/// Seed policy for the property-based suite.
/// </summary>
/// <remarks>
/// CI runs a fixed seed set on every push, so a red build is always reproducible and a green build
/// never went green by luck. Broad exploration is opt-in through the environment, which is what the
/// scheduled fuzz workflow sets — a nightly job that walks new seeds is where new bugs come from,
/// but it must never make the per-push signal flaky.
/// </remarks>
public static class FuzzConfig
{
    /// <summary>Environment variable that overrides the base seed.</summary>
    public const string SeedVariable = "PARQUET_FUZZ_SEED";

    /// <summary>Environment variable that overrides the number of cases per property.</summary>
    public const string CaseCountVariable = "PARQUET_FUZZ_CASES";

    /// <summary>
    /// The seed CI uses when nothing overrides it. Changing this changes which cases run on every
    /// push, so treat it the way you would treat a golden file.
    /// </summary>
    public const long DefaultBaseSeed = 169_0000_0001L;

    /// <summary>The number of cases per property when nothing overrides it.</summary>
    public const int DefaultCaseCount = 24;

    /// <summary>Gets the base seed in force for this run.</summary>
    public static long BaseSeed =>
        long.TryParse(
            Environment.GetEnvironmentVariable(SeedVariable),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long seed
        )
            ? seed
            : DefaultBaseSeed;

    /// <summary>Gets the number of cases per property in force for this run.</summary>
    public static int CaseCount =>
        int.TryParse(
            Environment.GetEnvironmentVariable(CaseCountVariable),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int count
        )
        && count > 0
            ? count
            : DefaultCaseCount;

    /// <summary>Gets a value indicating whether this run was pointed at a non-default seed.</summary>
    public static bool IsBroadRun =>
        Environment.GetEnvironmentVariable(SeedVariable) is not null
        || Environment.GetEnvironmentVariable(CaseCountVariable) is not null;

    /// <summary>The stride between consecutive seeds: the 64-bit golden-ratio constant.</summary>
    private const long GoldenRatioStride = unchecked((long)0x9E3779B97F4A7C15UL);

    /// <summary>Gets the seeds for this run, spread by the golden-ratio constant.</summary>
    public static IEnumerable<long> Seeds()
    {
        long baseSeed = BaseSeed;
        int count = CaseCount;
        for (int i = 0; i < count; i++)
        {
            yield return unchecked(baseSeed + (i * GoldenRatioStride));
        }
    }

    /// <summary>Gets the directory holding committed regression fixtures.</summary>
    public static string FixtureDirectory { get; } =
        Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "FuzzFixtures")
        );

    /// <summary>
    /// Gets the directory a failing run writes its minimised case into. Under the repository's
    /// gitignored <c>temp/</c> so a local failure leaves an artefact to promote into
    /// <see cref="FixtureDirectory"/>, without ever dirtying the tree on its own.
    /// </summary>
    public static string FailureDirectory { get; } =
        Path.GetFullPath(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "temp",
                "fuzz-failures"
            )
        );

    /// <summary>
    /// Writes a minimised failing case to <see cref="FailureDirectory"/> and returns its path, so
    /// the failure message can tell the reader exactly which file to copy into
    /// <see cref="FixtureDirectory"/> to make the regression permanent.
    /// </summary>
    public static string SaveFailure(FuzzCase minimized, string note)
    {
        Directory.CreateDirectory(FailureDirectory);
        string path = Path.Combine(
            FailureDirectory,
            $"fuzz-{minimized.Seed.ToString(CultureInfo.InvariantCulture)}.json"
        );
        FuzzCaseDocument document = FuzzCaseDocument.From(minimized);
        document.Note = note;
        IOFile.WriteAllText(
            path,
            System.Text.Json.JsonSerializer.Serialize(document, FuzzCaseDocument.Options)
        );
        return path;
    }
}

/// <summary>
/// Reduces a failing case to the smallest case that still fails.
/// </summary>
/// <remarks>
/// A random 500-row case across 28 columns says almost nothing about what broke. The same failure
/// pinned to one row of one column is a regression fixture someone can read. The shrinker only ever
/// makes a case smaller and only keeps a step that still fails, so its output is guaranteed to
/// reproduce the original problem.
/// </remarks>
public static class FuzzShrinker
{
    /// <summary>The most candidate cases a shrink will evaluate, to bound a failing run's cost.</summary>
    public const int MaxCandidates = 120;

    /// <summary>Shrinks <paramref name="failing"/> while <paramref name="stillFails"/> holds.</summary>
    public static async Task<FuzzCase> ShrinkAsync(
        FuzzCase failing,
        Func<FuzzCase, Task<string?>> stillFails
    )
    {
        FuzzCase best = failing;
        int budget = MaxCandidates;

        foreach (int rowCount in CandidateRowCounts(failing.RowCount))
        {
            if (budget-- <= 0)
            {
                return best;
            }

            if (rowCount >= best.RowCount)
            {
                continue;
            }

            FuzzCase candidate = best with { RowCount = rowCount };
            if (await stillFails(candidate) is not null)
            {
                best = candidate;
            }
        }

        bool progressed = true;
        while (progressed && budget > 0)
        {
            progressed = false;
            foreach (string column in best.Columns.ToList())
            {
                if (budget-- <= 0)
                {
                    break;
                }

                FuzzCase candidate = best with
                {
                    Columns = best.Columns.Where(c => c != column).ToList(),
                };
                if (candidate.Columns.Count == 0)
                {
                    continue;
                }

                if (await stillFails(candidate) is not null)
                {
                    best = candidate;
                    progressed = true;
                }
            }
        }

        foreach (FuzzCase candidate in SimplifiedKnobs(best))
        {
            if (budget-- <= 0)
            {
                break;
            }

            if (await stillFails(candidate) is not null)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static IEnumerable<int> CandidateRowCounts(int rowCount)
    {
        yield return 1;
        yield return 2;
        for (int n = rowCount / 2; n > 2; n /= 2)
        {
            yield return n;
        }

        yield return rowCount - 1;
    }

    private static IEnumerable<FuzzCase> SimplifiedKnobs(FuzzCase best)
    {
        if (best.EncodingHints.Count > 0)
        {
            yield return best with
            {
                EncodingHints = new Dictionary<string, ParquetColumnEncoding>(
                    StringComparer.Ordinal
                ),
            };
        }

        if (best.DeduplicateStrings)
        {
            yield return best with
            {
                DeduplicateStrings = false,
            };
        }

        if (best.Compression != ParquetCompressionMethod.None)
        {
            yield return best with
            {
                Compression = ParquetCompressionMethod.None,
            };
        }

        if (best.RowGroupSize != 50_000)
        {
            yield return best with
            {
                RowGroupSize = 50_000,
            };
        }
    }
}
