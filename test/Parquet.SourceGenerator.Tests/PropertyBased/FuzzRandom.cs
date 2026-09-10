using System;

namespace Parquet.SourceGenerator.Tests.PropertyBased;

/// <summary>
/// A small deterministic pseudo-random source (SplitMix64) used by the property-based suite.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="System.Random"/>. A reproducible seed is only worth anything if the
/// same seed yields the same case on every runtime and every future framework version, and
/// <see cref="System.Random"/> makes no such promise across implementations. SplitMix64 is fifteen
/// lines of arithmetic with no hidden state, so a seed printed by a failing CI run in 2026 still
/// reproduces the case locally years later.
/// </remarks>
public sealed class FuzzRandom
{
    private ulong _state;

    public FuzzRandom(long seed)
    {
        Seed = seed;
        _state = unchecked((ulong)seed + 0x9E3779B97F4A7C15UL);
    }

    /// <summary>Gets the seed this instance was created from.</summary>
    public long Seed { get; }

    /// <summary>Returns the next 64 random bits.</summary>
    public ulong NextULong()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Returns a non-negative integer below <paramref name="exclusiveUpperBound"/>.</summary>
    public int Next(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);

        return (int)(NextULong() % (ulong)exclusiveUpperBound);
    }

    /// <summary>Returns an integer in the inclusive-exclusive range.</summary>
    public int Next(int inclusiveLowerBound, int exclusiveUpperBound) =>
        inclusiveLowerBound + Next(exclusiveUpperBound - inclusiveLowerBound);

    /// <summary>Returns a double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Returns true with the given probability.</summary>
    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Picks one element uniformly.</summary>
    public T Pick<T>(ReadOnlySpan<T> items) => items[Next(items.Length)];

    /// <summary>Picks one element uniformly.</summary>
    public T Pick<T>(T[] items) => items[Next(items.Length)];

    /// <summary>Fills a buffer with random bytes.</summary>
    public void NextBytes(Span<byte> destination)
    {
        int i = 0;
        while (i < destination.Length)
        {
            ulong block = NextULong();
            for (int b = 0; b < 8 && i < destination.Length; b++, i++)
            {
                destination[i] = (byte)(block >> (b * 8));
            }
        }
    }

    /// <summary>Shuffles a list in place.</summary>
    public void Shuffle<T>(System.Collections.Generic.IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
