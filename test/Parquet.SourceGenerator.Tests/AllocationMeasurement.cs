using System;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Keeps allocation-sensitive async tests out of other xUnit collections. The allocation counter
/// is process-wide because an async operation may resume on more than one thread.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurementSuite
{
    public const string Name = "allocation-measurement";
}

internal readonly record struct AllocationMeasurementResult<T>(T Result, long AllocatedBytes);

internal static class AllocationMeasurement
{
    /// <summary>
    /// Measures exactly the supplied async operation, including allocations made after a
    /// continuation resumes on another thread.
    /// </summary>
    public static async Task<AllocationMeasurementResult<T>> MeasureAsync<T>(
        Func<Task<T>> operation
    )
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        T result = await operation().ConfigureAwait(false);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        return new(result, allocated);
    }
}
