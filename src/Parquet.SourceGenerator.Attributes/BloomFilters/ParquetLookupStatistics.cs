namespace Parquet.SourceGenerator.BloomFilters;

/// <summary>
/// Counts what a Bloom-filtered point lookup actually did, so the saving can be asserted in a test
/// or reported by a benchmark rather than assumed.
/// </summary>
public sealed class ParquetLookupStatistics
{
    /// <summary>Gets the number of row groups in the file.</summary>
    public int RowGroupsTotal { get; private set; }

    /// <summary>Gets the number of row groups whose pages were read.</summary>
    public int RowGroupsScanned { get; private set; }

    /// <summary>Gets the number of row groups eliminated by a Bloom filter probe.</summary>
    public int RowGroupsSkipped { get; private set; }

    /// <summary>
    /// Gets the number of scanned row groups that turned out not to hold the value — the Bloom
    /// filter's false positives, plus any row group that had no filter at all.
    /// </summary>
    public int FalsePositiveScans { get; private set; }

    /// <summary>Resets the counters for a file with the given number of row groups.</summary>
    public void Reset(int rowGroupsTotal)
    {
        RowGroupsTotal = rowGroupsTotal;
        RowGroupsScanned = 0;
        RowGroupsSkipped = 0;
        FalsePositiveScans = 0;
    }

    /// <summary>Records that a row group was eliminated without being read.</summary>
    public void RecordSkipped() => RowGroupsSkipped++;

    /// <summary>Records that a row group was read.</summary>
    public void RecordScanned() => RowGroupsScanned++;

    /// <summary>Records that a scanned row group produced no match.</summary>
    public void RecordFalsePositive() => FalsePositiveScans++;
}
