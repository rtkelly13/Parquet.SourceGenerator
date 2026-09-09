using System;
using System.Collections.Generic;

namespace Parquet.SourceGenerator.BloomFilters;

/// <summary>
/// Per-row-group Bloom filter lookup over an in-memory Parquet file, used to eliminate row groups
/// before any page is read or decompressed.
/// </summary>
/// <remarks>
/// Loading only walks the footer; a bitset is parsed the first time a probe needs it and then
/// cached, so a file whose row groups are all eliminated by the first few probes never touches the
/// remaining filters.
/// </remarks>
public sealed class ParquetBloomFilterIndex
{
    private static readonly ParquetBloomFilterIndex EmptyIndex = new(
        default,
        Array.Empty<IReadOnlyList<ParquetBloomFilterLocation>>()
    );

    private readonly ReadOnlyMemory<byte> _file;
    private readonly IReadOnlyList<IReadOnlyList<ParquetBloomFilterLocation>> _locations;
    private readonly Dictionary<long, ParquetSplitBlockBloomFilter> _cache = new();

    private ParquetBloomFilterIndex(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<IReadOnlyList<ParquetBloomFilterLocation>> locations
    )
    {
        _file = file;
        _locations = locations;
    }

    /// <summary>Gets the number of row groups the file declares.</summary>
    public int RowGroupCount => _locations.Count;

    /// <summary>Gets whether any column chunk in the file carries a Bloom filter.</summary>
    public bool HasAnyFilter
    {
        get
        {
            for (int r = 0; r < _locations.Count; r++)
            {
                IReadOnlyList<ParquetBloomFilterLocation> columns = _locations[r];
                for (int c = 0; c < columns.Count; c++)
                {
                    if (columns[c].HasFilter)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Walks the footer of <paramref name="parquetBytes"/> and indexes its Bloom filter placement.
    /// A malformed or filter-free file yields an index that never eliminates anything, so callers
    /// can use the result unconditionally.
    /// </summary>
    public static ParquetBloomFilterIndex Load(ReadOnlyMemory<byte> parquetBytes)
    {
        try
        {
            IReadOnlyList<IReadOnlyList<ParquetBloomFilterLocation>> locations =
                ParquetBloomFilterFooter.ReadLocations(parquetBytes.Span);
            return new ParquetBloomFilterIndex(parquetBytes, locations);
        }
        catch (FormatException)
        {
            return EmptyIndex;
        }
        catch (NotSupportedException)
        {
            return EmptyIndex;
        }
    }

    /// <summary>Gets whether the given column chunk carries a Bloom filter.</summary>
    public bool HasFilter(int rowGroupIndex, string columnPath) =>
        Find(rowGroupIndex, columnPath) != null;

    /// <summary>
    /// Probes the column chunk's Bloom filter. Returns <c>false</c> only when the filter proves the
    /// value is absent from that row group; a missing or unreadable filter yields <c>true</c>, so
    /// the caller falls back to reading the data.
    /// </summary>
    public bool MightContain(int rowGroupIndex, string columnPath, ulong hash)
    {
        ParquetBloomFilterLocation? location = Find(rowGroupIndex, columnPath);
        if (location == null)
        {
            return true;
        }

        long key = location.Offset!.Value;
        if (!_cache.TryGetValue(key, out ParquetSplitBlockBloomFilter? filter))
        {
            try
            {
                filter = ParquetBloomFilterFooter.ReadFilter(_file.Span, location);
            }
            catch (FormatException)
            {
                filter = null;
            }
            catch (NotSupportedException)
            {
                filter = null;
            }
            catch (ArgumentException)
            {
                filter = null;
            }

            _cache[key] = filter!;
        }

        return filter == null || filter.MightContain(hash);
    }

    private ParquetBloomFilterLocation? Find(int rowGroupIndex, string columnPath)
    {
        if (rowGroupIndex < 0 || rowGroupIndex >= _locations.Count)
        {
            return null;
        }

        IReadOnlyList<ParquetBloomFilterLocation> columns = _locations[rowGroupIndex];
        for (int c = 0; c < columns.Count; c++)
        {
            ParquetBloomFilterLocation candidate = columns[c];
            if (
                candidate.HasFilter
                && string.Equals(candidate.ColumnPath, columnPath, StringComparison.Ordinal)
            )
            {
                return candidate;
            }
        }

        return null;
    }
}
