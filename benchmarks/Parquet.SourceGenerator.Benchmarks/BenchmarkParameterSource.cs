using System.Globalization;

namespace Parquet.SourceGenerator.Benchmarks;

internal static class BenchmarkParameterSource
{
    public static IEnumerable<int> GetCounts(params int[] defaultCounts)
    {
        string? raw = Environment.GetEnvironmentVariable("PSG_BENCHMARK_COUNTS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultCounts;
        }

        int[] counts = raw.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Select(value =>
            {
                if (
                    !int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int count
                    )
                    || count <= 0
                )
                {
                    throw new InvalidOperationException(
                        $"Invalid benchmark Count value '{value}'."
                    );
                }

                return count;
            })
            .Distinct()
            .ToArray();

        return counts.Length == 0
            ? throw new InvalidOperationException("At least one benchmark Count is required.")
            : counts;
    }
}
