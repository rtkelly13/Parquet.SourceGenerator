using System;

namespace Parquet.SourceGenerator.BloomFilters;

/// <summary>
/// Hashes values the way the Parquet Bloom filter specification requires: xxHash64 with seed 0 over
/// the column's PLAIN encoding, not over the CLR object.
/// </summary>
/// <remarks>
/// Getting this wrong is silent and destructive — a mismatch between the write-side and read-side
/// hash turns a Bloom filter into a row-group eliminator that drops rows that really are present.
/// The overloads here are therefore the only supported way to produce a digest for
/// <see cref="ParquetSplitBlockBloomFilter"/>.
/// </remarks>
public static class ParquetBloomFilterHash
{
    /// <summary>Hashes an INT32 column value (4 bytes, little endian).</summary>
    public static ulong Of(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer[0] = (byte)value;
        buffer[1] = (byte)(value >> 8);
        buffer[2] = (byte)(value >> 16);
        buffer[3] = (byte)(value >> 24);
        return ParquetXxHash64.Hash(buffer);
    }

    /// <summary>Hashes an INT64 column value (8 bytes, little endian).</summary>
    public static ulong Of(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        for (int i = 0; i < 8; i++)
        {
            buffer[i] = (byte)(value >> (i * 8));
        }

        return ParquetXxHash64.Hash(buffer);
    }

    /// <summary>Hashes a BYTE_ARRAY column value carrying UTF-8 text.</summary>
    public static ulong Of(string value) => ParquetXxHash64.HashUtf8(value);

    /// <summary>Hashes a BYTE_ARRAY column value carrying raw bytes.</summary>
    public static ulong Of(byte[] value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return ParquetXxHash64.Hash(value);
    }

    /// <summary>
    /// Hashes a UUID column value as the 16-byte big-endian RFC 4122 layout Parquet stores.
    /// </summary>
    public static ulong Of(Guid value)
    {
        byte[] mixed = value.ToByteArray();
        Span<byte> buffer = stackalloc byte[16];
        buffer[0] = mixed[3];
        buffer[1] = mixed[2];
        buffer[2] = mixed[1];
        buffer[3] = mixed[0];
        buffer[4] = mixed[5];
        buffer[5] = mixed[4];
        buffer[6] = mixed[7];
        buffer[7] = mixed[6];
        for (int i = 8; i < 16; i++)
        {
            buffer[i] = mixed[i];
        }

        return ParquetXxHash64.Hash(buffer);
    }
}
