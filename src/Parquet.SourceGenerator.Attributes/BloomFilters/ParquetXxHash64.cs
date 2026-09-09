using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Parquet.SourceGenerator.BloomFilters;

/// <summary>
/// xxHash64 with seed 0 — the only hash the Parquet Bloom filter specification defines
/// (<c>BloomFilterHash.XXHASH</c>).
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from a package because the attributes assembly is the runtime
/// support library shipped alongside generated code: it targets netstandard2.0 through net9.0 and
/// deliberately carries no dependency beyond <c>System.Memory</c>. The algorithm is stable and
/// tiny, so vendoring it costs less than a dependency that would have to be resolved on .NET
/// Framework consumers as well.
/// </remarks>
public static class ParquetXxHash64
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    /// <summary>
    /// Computes the xxHash64 digest of <paramref name="data"/> using the specification's seed of 0.
    /// </summary>
    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        int len = data.Length;
        ulong h64;
        int index = 0;

        if (len >= 32)
        {
            ulong v1 = unchecked(Prime1 + Prime2);
            ulong v2 = Prime2;
            ulong v3 = 0;
            ulong v4 = unchecked(0UL - Prime1);

            do
            {
                v1 = Round(v1, ReadUInt64(data, index));
                v2 = Round(v2, ReadUInt64(data, index + 8));
                v3 = Round(v3, ReadUInt64(data, index + 16));
                v4 = Round(v4, ReadUInt64(data, index + 24));
                index += 32;
            } while (index <= len - 32);

            h64 = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
            h64 = MergeRound(h64, v1);
            h64 = MergeRound(h64, v2);
            h64 = MergeRound(h64, v3);
            h64 = MergeRound(h64, v4);
        }
        else
        {
            h64 = Prime5;
        }

        h64 = unchecked(h64 + (ulong)len);

        while (index <= len - 8)
        {
            ulong k1 = Round(0, ReadUInt64(data, index));
            h64 ^= k1;
            h64 = unchecked(RotateLeft(h64, 27) * Prime1 + Prime4);
            index += 8;
        }

        if (index <= len - 4)
        {
            h64 ^= unchecked(ReadUInt32(data, index) * Prime1);
            h64 = unchecked(RotateLeft(h64, 23) * Prime2 + Prime3);
            index += 4;
        }

        while (index < len)
        {
            h64 ^= unchecked(data[index] * Prime5);
            h64 = unchecked(RotateLeft(h64, 11) * Prime1);
            index++;
        }

        h64 ^= h64 >> 33;
        h64 = unchecked(h64 * Prime2);
        h64 ^= h64 >> 29;
        h64 = unchecked(h64 * Prime3);
        h64 ^= h64 >> 32;
        return h64;
    }

    /// <summary>
    /// Hashes a UTF-8 encoded string exactly as the specification requires for BYTE_ARRAY columns:
    /// over the encoded bytes, with no length prefix.
    /// </summary>
    public static ulong HashUtf8(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte[] buffer = new byte[byteCount];
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
        return Hash(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong acc, ulong input)
    {
        acc = unchecked(acc + input * Prime2);
        acc = RotateLeft(acc, 31);
        return unchecked(acc * Prime1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MergeRound(ulong acc, ulong val)
    {
        val = Round(0, val);
        acc ^= val;
        return unchecked(acc * Prime1 + Prime4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateLeft(ulong value, int offset) =>
        (value << offset) | (value >> (64 - offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int index) =>
        (ulong)data[index]
        | ((ulong)data[index + 1] << 8)
        | ((ulong)data[index + 2] << 16)
        | ((ulong)data[index + 3] << 24)
        | ((ulong)data[index + 4] << 32)
        | ((ulong)data[index + 5] << 40)
        | ((ulong)data[index + 6] << 48)
        | ((ulong)data[index + 7] << 56);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadUInt32(ReadOnlySpan<byte> data, int index) =>
        (ulong)(
            data[index]
            | ((uint)data[index + 1] << 8)
            | ((uint)data[index + 2] << 16)
            | ((uint)data[index + 3] << 24)
        );
}
