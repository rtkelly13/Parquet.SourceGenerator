using System;

namespace Parquet.SourceGenerator.BloomFilters;

/// <summary>
/// The split-block Bloom filter defined by the Apache Parquet format specification
/// (<c>BloomFilterAlgorithm.BLOCK</c>): a bitset partitioned into 32-byte blocks of eight 32-bit
/// words, where a single insert or probe touches exactly one cache-line-sized block.
/// </summary>
/// <remarks>
/// The structure has no false negatives: a probe returning <c>false</c> proves the value was never
/// inserted, which is what makes whole-row-group elimination sound. A probe returning <c>true</c>
/// may be a false positive, so callers must still verify against the data.
/// </remarks>
public sealed class ParquetSplitBlockBloomFilter
{
    /// <summary>Number of 32-bit words in one block; fixed by the specification.</summary>
    public const int WordsPerBlock = 8;

    /// <summary>Number of bytes in one block; fixed by the specification.</summary>
    public const int BytesPerBlock = WordsPerBlock * sizeof(uint);

    /// <summary>
    /// The odd multipliers the specification uses to derive one bit position per word. Changing
    /// these changes the on-disk meaning of every filter, so they are not configurable.
    /// </summary>
    private static readonly uint[] Salt =
    {
        0x47b6137bu,
        0x44974d91u,
        0x8824ad5bu,
        0xa2b7289du,
        0x705495c7u,
        0x2df1424bu,
        0x9efc4947u,
        0x5c6bfb31u,
    };

    private readonly uint[] _words;

    private ParquetSplitBlockBloomFilter(uint[] words)
    {
        _words = words;
    }

    /// <summary>Gets the number of 32-byte blocks in the bitset.</summary>
    public int BlockCount => _words.Length / WordsPerBlock;

    /// <summary>Gets the size of the serialized bitset in bytes.</summary>
    public int ByteCount => _words.Length * sizeof(uint);

    /// <summary>
    /// Creates an empty filter with an explicit bitset size, which must be a positive multiple of
    /// <see cref="BytesPerBlock"/>.
    /// </summary>
    public static ParquetSplitBlockBloomFilter WithByteSize(int byteSize)
    {
        if (byteSize <= 0 || byteSize % BytesPerBlock != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteSize),
                byteSize,
                "Bloom filter size must be a positive multiple of " + BytesPerBlock + " bytes."
            );
        }

        return new ParquetSplitBlockBloomFilter(new uint[byteSize / sizeof(uint)]);
    }

    /// <summary>
    /// Creates an empty filter sized for <paramref name="expectedDistinctValues"/> entries at the
    /// requested false-positive probability, rounded up to a power-of-two block count.
    /// </summary>
    /// <param name="expectedDistinctValues">Expected number of distinct values inserted.</param>
    /// <param name="falsePositiveProbability">Target FPP, exclusive of 0 and 1. Defaults to 1%.</param>
    /// <remarks>
    /// Uses the sizing formula from the format specification's reference implementation. The
    /// result is clamped to at least one block and at most 128 MiB, which is the cap parquet-mr
    /// applies.
    /// </remarks>
    public static ParquetSplitBlockBloomFilter ForExpectedItems(
        long expectedDistinctValues,
        double falsePositiveProbability = 0.01
    )
    {
        if (expectedDistinctValues < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedDistinctValues));
        }

        if (falsePositiveProbability <= 0 || falsePositiveProbability >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(falsePositiveProbability));
        }

        double n = Math.Max(1, expectedDistinctValues);
        double m = -8 * n / Math.Log(1 - Math.Pow(falsePositiveProbability, 1.0 / 8));
        double bytes = m / 8;

        long blocks = 1;
        while (blocks * BytesPerBlock < bytes && blocks < (128L * 1024 * 1024 / BytesPerBlock))
        {
            blocks <<= 1;
        }

        return WithByteSize((int)(blocks * BytesPerBlock));
    }

    /// <summary>
    /// Rehydrates a filter from its serialized little-endian bitset.
    /// </summary>
    public static ParquetSplitBlockBloomFilter FromBytes(ReadOnlySpan<byte> bitset)
    {
        if (bitset.Length <= 0 || bitset.Length % BytesPerBlock != 0)
        {
            throw new ArgumentException(
                "Bloom filter bitset length must be a positive multiple of "
                    + BytesPerBlock
                    + " bytes.",
                nameof(bitset)
            );
        }

        var words = new uint[bitset.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            int o = i * 4;
            words[i] =
                bitset[o]
                | ((uint)bitset[o + 1] << 8)
                | ((uint)bitset[o + 2] << 16)
                | ((uint)bitset[o + 3] << 24);
        }

        return new ParquetSplitBlockBloomFilter(words);
    }

    /// <summary>Serializes the bitset as little-endian 32-bit words.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[ByteCount];
        WriteTo(bytes);
        return bytes;
    }

    /// <summary>Serializes the bitset into <paramref name="destination"/>.</summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < ByteCount)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        for (int i = 0; i < _words.Length; i++)
        {
            uint w = _words[i];
            int o = i * 4;
            destination[o] = (byte)w;
            destination[o + 1] = (byte)(w >> 8);
            destination[o + 2] = (byte)(w >> 16);
            destination[o + 3] = (byte)(w >> 24);
        }
    }

    /// <summary>Inserts a pre-computed xxHash64 digest.</summary>
    public void Insert(ulong hash)
    {
        int block = BlockIndex(hash);
        uint key = (uint)hash;
        int baseIndex = block * WordsPerBlock;
        for (int i = 0; i < WordsPerBlock; i++)
        {
            _words[baseIndex + i] |= MaskWord(key, i);
        }
    }

    /// <summary>
    /// Probes a pre-computed xxHash64 digest. <c>false</c> is conclusive: the value was never
    /// inserted. <c>true</c> means "possibly present".
    /// </summary>
    public bool MightContain(ulong hash)
    {
        int block = BlockIndex(hash);
        uint key = (uint)hash;
        int baseIndex = block * WordsPerBlock;
        for (int i = 0; i < WordsPerBlock; i++)
        {
            if ((_words[baseIndex + i] & MaskWord(key, i)) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private int BlockIndex(ulong hash)
    {
        // The specification selects the block from the high 32 bits, scaled by the block count
        // with a 64-bit multiply-shift rather than a modulo.
        ulong high = hash >> 32;
        return (int)((high * (ulong)BlockCount) >> 32);
    }

    private static uint MaskWord(uint key, int wordIndex)
    {
        uint y = unchecked(key * Salt[wordIndex]);
        return 1u << (int)(y >> 27);
    }
}
