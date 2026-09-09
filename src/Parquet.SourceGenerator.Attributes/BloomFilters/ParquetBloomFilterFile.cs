using System;
using System.Collections.Generic;

namespace Parquet.SourceGenerator.BloomFilters;

/// <summary>
/// One column chunk's Bloom filter placement, as recorded in the Parquet footer.
/// </summary>
public sealed class ParquetBloomFilterLocation
{
    internal ParquetBloomFilterLocation(string columnPath, long? offset, int? length)
    {
        ColumnPath = columnPath;
        Offset = offset;
        Length = length;
    }

    /// <summary>Gets the dotted <c>path_in_schema</c> of the column chunk.</summary>
    public string ColumnPath { get; }

    /// <summary>Gets the absolute file offset of the Bloom filter header, when one is present.</summary>
    public long? Offset { get; }

    /// <summary>Gets the total serialized size of header plus bitset, when the writer recorded it.</summary>
    public int? Length { get; }

    /// <summary>Gets whether this column chunk carries a Bloom filter.</summary>
    public bool HasFilter => Offset.HasValue;
}

/// <summary>
/// A Bloom filter to attach to one column chunk of an already-written Parquet file.
/// </summary>
public sealed class ParquetBloomFilterEntry
{
    /// <summary>Creates an entry targeting a column chunk by row group index and column path.</summary>
    public ParquetBloomFilterEntry(
        int rowGroupIndex,
        string columnPath,
        ParquetSplitBlockBloomFilter filter
    )
    {
        if (rowGroupIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowGroupIndex));
        }

        RowGroupIndex = rowGroupIndex;
        ColumnPath = columnPath ?? throw new ArgumentNullException(nameof(columnPath));
        Filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    /// <summary>Gets the zero-based row group index.</summary>
    public int RowGroupIndex { get; }

    /// <summary>Gets the dotted <c>path_in_schema</c> identifying the column chunk.</summary>
    public string ColumnPath { get; }

    /// <summary>Gets the filter to serialize.</summary>
    public ParquetSplitBlockBloomFilter Filter { get; }
}

/// <summary>
/// Reads and rewrites the Bloom filter placement recorded in a Parquet file footer.
/// </summary>
/// <remarks>
/// Parquet.Net 6 parses <c>bloom_filter_offset</c> into <c>ColumnMetaData</c> but never writes one
/// and never probes one. This type supplies both halves over the raw file bytes, so no fork of the
/// library is required: filters are appended after the last row group and before the footer, which
/// is exactly where parquet-mr and parquet-cpp put them, and the footer is re-serialized with the
/// offsets spliced in. Every data page offset already recorded in the footer stays valid because
/// nothing before the footer moves.
/// </remarks>
public static class ParquetBloomFilterFooter
{
    private const int MagicLength = 4;
    private const int FooterLengthSize = 4;

    private static readonly byte[] Magic = { (byte)'P', (byte)'A', (byte)'R', (byte)'1' };

    /// <summary>
    /// Reads, per row group, the column chunk paths and their Bloom filter placement.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ParquetBloomFilterLocation>> ReadLocations(
        ReadOnlySpan<byte> parquetBytes
    )
    {
        GetFooterRange(parquetBytes, out int footerStart, out int footerLength);
        var reader = new ThriftCompactReader(parquetBytes.Slice(footerStart, footerLength));
        return ReadRowGroups(ref reader);
    }

    /// <summary>
    /// Appends the supplied Bloom filters to a written Parquet file and rewrites the footer so each
    /// targeted column chunk records its offset and length.
    /// </summary>
    /// <returns>A new file image. The input is not modified.</returns>
    public static byte[] Attach(
        ReadOnlySpan<byte> parquetBytes,
        IReadOnlyList<ParquetBloomFilterEntry> entries
    )
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        GetFooterRange(parquetBytes, out int footerStart, out int footerLength);

        if (entries.Count == 0)
        {
            return parquetBytes.ToArray();
        }

        IReadOnlyList<IReadOnlyList<ParquetBloomFilterLocation>> locations = ReadLocations(
            parquetBytes
        );

        // Serialize every filter into one contiguous block that will sit between the last row group
        // and the footer, recording where each landed.
        var payload = new List<byte>(entries.Count * 1024);
        var injections = new Dictionary<int, Dictionary<int, KeyValuePair<long, int>>>();

        foreach (ParquetBloomFilterEntry entry in entries)
        {
            if (entry.RowGroupIndex >= locations.Count)
            {
                throw new ArgumentException(
                    "Row group "
                        + entry.RowGroupIndex
                        + " does not exist in the supplied Parquet file.",
                    nameof(entries)
                );
            }

            IReadOnlyList<ParquetBloomFilterLocation> columns = locations[entry.RowGroupIndex];
            int columnIndex = -1;
            for (int i = 0; i < columns.Count; i++)
            {
                if (
                    string.Equals(columns[i].ColumnPath, entry.ColumnPath, StringComparison.Ordinal)
                )
                {
                    columnIndex = i;
                    break;
                }
            }

            if (columnIndex < 0)
            {
                throw new ArgumentException(
                    "Column '" + entry.ColumnPath + "' is not present in the Parquet schema.",
                    nameof(entries)
                );
            }

            long offset = footerStart + payload.Count;
            int before = payload.Count;
            WriteBloomFilterHeader(payload, entry.Filter.ByteCount);
            payload.AddRange(entry.Filter.ToBytes());
            int totalLength = payload.Count - before;

            if (!injections.TryGetValue(entry.RowGroupIndex, out var perColumn))
            {
                perColumn = new Dictionary<int, KeyValuePair<long, int>>();
                injections[entry.RowGroupIndex] = perColumn;
            }

            perColumn[columnIndex] = new KeyValuePair<long, int>(offset, totalLength);
        }

        byte[] newFooter = RewriteFooter(
            parquetBytes.Slice(footerStart, footerLength),
            injections,
            payload.Count
        );

        var result = new byte[
            footerStart + payload.Count + newFooter.Length + FooterLengthSize + MagicLength
        ];
        parquetBytes.Slice(0, footerStart).CopyTo(result);
        payload.CopyTo(result, footerStart);
        Array.Copy(newFooter, 0, result, footerStart + payload.Count, newFooter.Length);

        int tail = footerStart + payload.Count + newFooter.Length;
        result[tail] = (byte)newFooter.Length;
        result[tail + 1] = (byte)(newFooter.Length >> 8);
        result[tail + 2] = (byte)(newFooter.Length >> 16);
        result[tail + 3] = (byte)(newFooter.Length >> 24);
        Array.Copy(Magic, 0, result, tail + FooterLengthSize, MagicLength);
        return result;
    }

    /// <summary>
    /// Reads the bitset of the Bloom filter that <paramref name="location"/> points at.
    /// </summary>
    public static ParquetSplitBlockBloomFilter ReadFilter(
        ReadOnlySpan<byte> parquetBytes,
        ParquetBloomFilterLocation location
    )
    {
        if (location == null)
        {
            throw new ArgumentNullException(nameof(location));
        }

        if (!location.Offset.HasValue)
        {
            throw new ArgumentException("The column chunk has no Bloom filter.", nameof(location));
        }

        int offset = checked((int)location.Offset.Value);
        var reader = new ThriftCompactReader(parquetBytes.Slice(offset));
        int numBytes = ReadBloomFilterHeader(ref reader);
        int bitsetStart = offset + reader.Position;
        return ParquetSplitBlockBloomFilter.FromBytes(parquetBytes.Slice(bitsetStart, numBytes));
    }

    private static void GetFooterRange(
        ReadOnlySpan<byte> parquetBytes,
        out int footerStart,
        out int footerLength
    )
    {
        if (parquetBytes.Length < 2 * MagicLength + FooterLengthSize)
        {
            throw new FormatException("Buffer is too small to be a Parquet file.");
        }

        int tail = parquetBytes.Length - MagicLength;
        if (
            parquetBytes[tail] != Magic[0]
            || parquetBytes[tail + 1] != Magic[1]
            || parquetBytes[tail + 2] != Magic[2]
            || parquetBytes[tail + 3] != Magic[3]
        )
        {
            throw new FormatException("Buffer does not end with the Parquet magic bytes.");
        }

        int lengthAt = tail - FooterLengthSize;
        footerLength =
            parquetBytes[lengthAt]
            | (parquetBytes[lengthAt + 1] << 8)
            | (parquetBytes[lengthAt + 2] << 16)
            | (parquetBytes[lengthAt + 3] << 24);

        footerStart = lengthAt - footerLength;
        if (footerStart < MagicLength)
        {
            throw new FormatException("Parquet footer length is out of range.");
        }
    }

    private static List<IReadOnlyList<ParquetBloomFilterLocation>> ReadRowGroups(
        ref ThriftCompactReader reader
    )
    {
        var rowGroups = new List<IReadOnlyList<ParquetBloomFilterLocation>>();
        reader.LastFieldId = 0;
        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            if (id == 4 && type == ThriftType.List)
            {
                reader.ReadListHeader(out int size, out _);
                for (int r = 0; r < size; r++)
                {
                    rowGroups.Add(ReadRowGroup(ref reader));
                }
            }
            else
            {
                reader.SkipValue(type);
            }
        }

        return rowGroups;
    }

    private static List<ParquetBloomFilterLocation> ReadRowGroup(ref ThriftCompactReader reader)
    {
        var columns = new List<ParquetBloomFilterLocation>();
        short saved = reader.LastFieldId;
        reader.LastFieldId = 0;
        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            if (id == 1 && type == ThriftType.List)
            {
                reader.ReadListHeader(out int size, out _);
                for (int c = 0; c < size; c++)
                {
                    columns.Add(ReadColumnChunk(ref reader));
                }
            }
            else
            {
                reader.SkipValue(type);
            }
        }

        reader.LastFieldId = saved;
        return columns;
    }

    private static ParquetBloomFilterLocation ReadColumnChunk(ref ThriftCompactReader reader)
    {
        ParquetBloomFilterLocation? location = null;
        short saved = reader.LastFieldId;
        reader.LastFieldId = 0;
        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            if (id == 3 && type == ThriftType.Struct)
            {
                location = ReadColumnMetaData(ref reader);
            }
            else
            {
                reader.SkipValue(type);
            }
        }

        reader.LastFieldId = saved;
        return location ?? new ParquetBloomFilterLocation(string.Empty, null, null);
    }

    private static ParquetBloomFilterLocation ReadColumnMetaData(ref ThriftCompactReader reader)
    {
        string path = string.Empty;
        long? offset = null;
        int? length = null;

        short saved = reader.LastFieldId;
        reader.LastFieldId = 0;
        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            switch (id)
            {
                case 3 when type == ThriftType.List:
                {
                    reader.ReadListHeader(out int size, out byte elementType);
                    var parts = new string[size];
                    for (int i = 0; i < size; i++)
                    {
                        parts[i] =
                            elementType == ThriftType.Binary ? reader.ReadString() : string.Empty;
                    }

                    path = string.Join(".", parts);
                    break;
                }
                case 14 when type == ThriftType.I64:
                    offset = reader.ReadI64();
                    break;
                case 15 when type == ThriftType.I32:
                    length = reader.ReadI32();
                    break;
                default:
                    reader.SkipValue(type);
                    break;
            }
        }

        reader.LastFieldId = saved;
        return new ParquetBloomFilterLocation(path, offset, length);
    }

    private static byte[] RewriteFooter(
        ReadOnlySpan<byte> footer,
        Dictionary<int, Dictionary<int, KeyValuePair<long, int>>> injections,
        int payloadLength
    )
    {
        var reader = new ThriftCompactReader(footer);
        var writer = new ThriftCompactWriter(footer.Length + payloadLength / 64 + 256);

        reader.LastFieldId = 0;
        writer.LastFieldId = 0;
        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            if (id == 4 && type == ThriftType.List)
            {
                writer.WriteFieldHeader(id, type);
                reader.ReadListHeader(out int size, out byte elementType);
                writer.WriteListHeader(size, elementType);
                for (int r = 0; r < size; r++)
                {
                    injections.TryGetValue(r, out var perColumn);
                    CopyRowGroup(ref reader, writer, footer, perColumn);
                }
            }
            else
            {
                CopyRawField(ref reader, writer, footer, id, type);
            }
        }

        writer.WriteStop();
        return writer.ToArray();
    }

    private static void CopyRowGroup(
        ref ThriftCompactReader reader,
        ThriftCompactWriter writer,
        ReadOnlySpan<byte> footer,
        Dictionary<int, KeyValuePair<long, int>>? perColumn
    )
    {
        short savedRead = reader.LastFieldId;
        short savedWrite = writer.LastFieldId;
        reader.LastFieldId = 0;
        writer.LastFieldId = 0;

        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            if (id == 1 && type == ThriftType.List)
            {
                writer.WriteFieldHeader(id, type);
                reader.ReadListHeader(out int size, out byte elementType);
                writer.WriteListHeader(size, elementType);
                for (int c = 0; c < size; c++)
                {
                    KeyValuePair<long, int>? placement = null;
                    if (perColumn != null && perColumn.TryGetValue(c, out var found))
                    {
                        placement = found;
                    }

                    CopyColumnChunk(ref reader, writer, footer, placement);
                }
            }
            else
            {
                CopyRawField(ref reader, writer, footer, id, type);
            }
        }

        writer.WriteStop();
        reader.LastFieldId = savedRead;
        writer.LastFieldId = savedWrite;
    }

    private static void CopyColumnChunk(
        ref ThriftCompactReader reader,
        ThriftCompactWriter writer,
        ReadOnlySpan<byte> footer,
        KeyValuePair<long, int>? placement
    )
    {
        short savedRead = reader.LastFieldId;
        short savedWrite = writer.LastFieldId;
        reader.LastFieldId = 0;
        writer.LastFieldId = 0;

        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            if (id == 3 && type == ThriftType.Struct && placement.HasValue)
            {
                writer.WriteFieldHeader(id, type);
                CopyColumnMetaData(ref reader, writer, footer, placement.Value);
            }
            else
            {
                CopyRawField(ref reader, writer, footer, id, type);
            }
        }

        writer.WriteStop();
        reader.LastFieldId = savedRead;
        writer.LastFieldId = savedWrite;
    }

    private static void CopyColumnMetaData(
        ref ThriftCompactReader reader,
        ThriftCompactWriter writer,
        ReadOnlySpan<byte> footer,
        KeyValuePair<long, int> placement
    )
    {
        short savedRead = reader.LastFieldId;
        short savedWrite = writer.LastFieldId;
        reader.LastFieldId = 0;
        writer.LastFieldId = 0;

        bool injected = false;
        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            // Fields are emitted in ascending id order, so 14/15 go in just before the first field
            // that outranks them — or at the stop marker when there is none.
            if (!injected && id > 15)
            {
                WriteBloomPlacement(writer, placement);
                injected = true;
            }

            if (id == 14 || id == 15)
            {
                // Replace any placement a previous pass wrote.
                reader.SkipValue(type);
                continue;
            }

            CopyRawField(ref reader, writer, footer, id, type);
        }

        if (!injected)
        {
            WriteBloomPlacement(writer, placement);
        }

        writer.WriteStop();
        reader.LastFieldId = savedRead;
        writer.LastFieldId = savedWrite;
    }

    private static void WriteBloomPlacement(
        ThriftCompactWriter writer,
        KeyValuePair<long, int> placement
    )
    {
        writer.WriteFieldHeader(14, ThriftType.I64);
        writer.WriteI64(placement.Key);
        writer.WriteFieldHeader(15, ThriftType.I32);
        writer.WriteI32(placement.Value);
    }

    private static void CopyRawField(
        ref ThriftCompactReader reader,
        ThriftCompactWriter writer,
        ReadOnlySpan<byte> footer,
        short id,
        byte type
    )
    {
        int start = reader.Position;
        reader.SkipValue(type);
        int end = reader.Position;
        writer.WriteFieldHeader(id, type);
        if (end > start)
        {
            writer.WriteRaw(footer.Slice(start, end - start));
        }
    }

    private static void WriteBloomFilterHeader(List<byte> destination, int numBytes)
    {
        var writer = new ThriftCompactWriter(32);
        writer.WriteFieldHeader(1, ThriftType.I32);
        writer.WriteI32(numBytes);

        // algorithm = BLOCK, hash = XXHASH, compression = UNCOMPRESSED — each a union holding a
        // single empty struct, which is the only combination the format currently defines.
        for (short field = 2; field <= 4; field++)
        {
            writer.WriteFieldHeader(field, ThriftType.Struct);
            short saved = writer.LastFieldId;
            writer.LastFieldId = 0;
            writer.WriteFieldHeader(1, ThriftType.Struct);
            writer.WriteStop();
            writer.WriteStop();
            writer.LastFieldId = saved;
        }

        writer.WriteStop();
        writer.CopyTo(destination);
    }

    private static int ReadBloomFilterHeader(ref ThriftCompactReader reader)
    {
        int numBytes = -1;
        int algorithm = -1;
        int hash = -1;
        int compression = -1;

        reader.LastFieldId = 0;
        while (reader.ReadFieldHeader(out short id, out byte type))
        {
            switch (id)
            {
                case 1 when type == ThriftType.I32:
                    numBytes = reader.ReadI32();
                    break;
                case 2:
                    algorithm = ReadUnionTag(ref reader, type);
                    break;
                case 3:
                    hash = ReadUnionTag(ref reader, type);
                    break;
                case 4:
                    compression = ReadUnionTag(ref reader, type);
                    break;
                default:
                    reader.SkipValue(type);
                    break;
            }
        }

        if (numBytes <= 0)
        {
            throw new FormatException("Bloom filter header declares a non-positive bitset size.");
        }

        if (algorithm != 1 || hash != 1 || compression != 1)
        {
            throw new NotSupportedException(
                "Only SPLIT_BLOCK / XXHASH / UNCOMPRESSED Bloom filters are supported (read "
                    + algorithm
                    + "/"
                    + hash
                    + "/"
                    + compression
                    + ")."
            );
        }

        return numBytes;
    }

    private static int ReadUnionTag(ref ThriftCompactReader reader, byte type)
    {
        if (type != ThriftType.Struct)
        {
            reader.SkipValue(type);
            return -1;
        }

        short saved = reader.LastFieldId;
        reader.LastFieldId = 0;
        int tag = -1;
        while (reader.ReadFieldHeader(out short id, out byte innerType))
        {
            tag = id;
            reader.SkipValue(innerType);
        }

        reader.LastFieldId = saved;
        return tag;
    }
}
