using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Equivalence and correctness suite for the branchless / SIMD null bitmap construction
/// helpers (issue #145). Every vectorised routine is asserted byte-identical against a plain
/// scalar reference over shapes that exercise the vector tail: all-null, no-null, alternating,
/// clustered, and lengths that are not multiples of the 16-element block width.
/// </summary>
public sealed class NullableColumnExtractorTests
{
    private static readonly int[] Lengths =
    [
        0,
        1,
        2,
        3,
        7,
        8,
        15,
        16,
        17,
        23,
        31,
        32,
        33,
        47,
        63,
        64,
        65,
        127,
        128,
        129,
        1000,
        1023,
        1024,
        1025,
        4097,
    ];

    public static TheoryData<int> AllLengths
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (int n in Lengths)
                data.Add(n);
            return data;
        }
    }

    /// <summary>Null-density shapes, expressed as a predicate over the element index.</summary>
    private static IEnumerable<(string Name, Func<int, bool> Present)> Shapes()
    {
        yield return ("all-null", _ => false);
        yield return ("no-null", _ => true);
        yield return ("alternating", i => i % 2 == 0);
        yield return ("alternating-offset", i => i % 2 == 1);
        yield return ("every-third", i => i % 3 != 0);
        yield return ("sparse-10pct-null", i => i % 10 != 0);
        yield return ("dense-null-80pct", i => i % 5 == 0);
        yield return ("first-block-full", i => i < 16);
        yield return ("first-block-empty", i => i >= 16);
        yield return ("clustered", i => (i / 16) % 2 == 0);
        yield return ("clustered-odd", i => (i / 17) % 2 == 0);
        yield return ("only-last", i => false);
    }

    private static int?[] Build(int count, Func<int, bool> present, bool forceLastPresent = false)
    {
        var source = new int?[count];
        for (int i = 0; i < count; i++)
        {
            bool p = present(i) || (forceLastPresent && i == count - 1);
            source[i] = p ? (i * 7919) - 4096 : (int?)null;
        }

        return source;
    }

    /// <summary>Independent reference: the branchy shape the generator used to emit.</summary>
    private static (int[] Levels, int[] Values, int Count) ScalarReference(int?[] source)
    {
        var levels = new int[source.Length];
        var values = new int[source.Length];
        int packed = 0;
        for (int i = 0; i < source.Length; i++)
        {
            int? v = source[i];
            if (v.HasValue)
            {
                values[packed++] = v.Value;
                levels[i] = 1;
            }
            else
            {
                levels[i] = 0;
            }
        }

        return (levels, values, packed);
    }

    [Theory]
    [MemberData(nameof(AllLengths))]
    public void BranchlessAndTwoPassMatchBranchyScalarReference(int count)
    {
        foreach ((string name, Func<int, bool> present) in Shapes())
        {
            foreach (bool forceLast in new[] { false, true })
            {
                int?[] source = Build(count, present, forceLast);
                (int[] expectedLevels, int[] expectedValues, int expectedCount) = ScalarReference(
                    source
                );

                var branchlessLevels = new int[count];
                var branchlessValues = new int[count];
                int branchlessCount = NullableColumnExtractor.ExtractBranchless<int>(
                    source,
                    branchlessLevels,
                    branchlessValues
                );

                var twoPassLevels = new int[count];
                var twoPassValues = new int[count];
                var presence = new byte[count];
                int twoPassCount = NullableColumnExtractor.ExtractTwoPass<int>(
                    source,
                    presence,
                    twoPassLevels,
                    twoPassValues
                );

                string because = $"{name}/len={count}/forceLast={forceLast}";
                Assert.Equal(expectedCount, branchlessCount);
                Assert.Equal(expectedCount, twoPassCount);
                Assert.True(expectedLevels.SequenceEqual(branchlessLevels), because);
                Assert.True(expectedLevels.SequenceEqual(twoPassLevels), because);
                Assert.True(
                    expectedValues
                        .Take(expectedCount)
                        .SequenceEqual(branchlessValues.Take(branchlessCount)),
                    because
                );
                Assert.True(
                    expectedValues
                        .Take(expectedCount)
                        .SequenceEqual(twoPassValues.Take(twoPassCount)),
                    because
                );
                Assert.Equal(expectedCount, NullableColumnExtractor.CountPresent(presence));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllLengths))]
    public void ExpandPresenceMatchesScalarWiden(int count)
    {
        var presence = new byte[count];
        for (int i = 0; i < count; i++)
            presence[i] = (byte)((i * 2654435761u) % 3 == 0 ? 1 : 0);

        var vector = new int[count];
        NullableColumnExtractor.ExpandPresenceToDefinitionLevels(presence, vector);

        for (int i = 0; i < count; i++)
            Assert.Equal(presence[i], vector[i]);
    }

    [Theory]
    [MemberData(nameof(AllLengths))]
    public void CountPresentMatchesScalarSum(int count)
    {
        var presence = new byte[count];
        int expected = 0;
        for (int i = 0; i < count; i++)
        {
            presence[i] = (byte)(i % 7 == 0 ? 0 : 1);
            expected += presence[i];
        }

        Assert.Equal(expected, NullableColumnExtractor.CountPresent(presence));
    }

    [Fact]
    public void CountPresentHandlesAccumulatorDrainBoundary()
    {
        // 255 blocks of 16 all-present bytes is exactly the byte-lane saturation point.
        foreach (int count in new[] { 16 * 254, 16 * 255, (16 * 255) + 1, 16 * 256, 16 * 600 })
        {
            var presence = new byte[count];
            presence.AsSpan().Fill(1);
            Assert.Equal(count, NullableColumnExtractor.CountPresent(presence));
        }
    }

    [Theory]
    [MemberData(nameof(AllLengths))]
    public void CompactInPlaceMatchesScalarFilter(int count)
    {
        foreach ((string name, Func<int, bool> present) in Shapes())
        {
            var presence = new byte[count];
            var values = new long[count];
            var expected = new List<long>();
            for (int i = 0; i < count; i++)
            {
                values[i] = (long)i * 1_000_003L;
                presence[i] = present(i) ? (byte)1 : (byte)0;
                if (presence[i] != 0)
                    expected.Add(values[i]);
            }

            int packed = NullableColumnExtractor.CompactInPlace<long>(values, presence);
            Assert.Equal(expected.Count, packed);
            Assert.True(expected.SequenceEqual(values.Take(packed)), $"{name}/len={count}");
        }
    }

    [Fact]
    public void WorksForNonIntegralAndWideElementTypes()
    {
        const int Count = 100;
        var source = new DateTime?[Count];
        var guids = new Guid?[Count];
        for (int i = 0; i < Count; i++)
        {
            bool present = i % 4 != 0;
            source[i] = present
                ? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i)
                : null;
            guids[i] = present ? Guid.Parse($"00000000-0000-0000-0000-{i:D12}") : null;
        }

        var levels = new int[Count];
        var dates = new DateTime[Count];
        int packedDates = NullableColumnExtractor.ExtractBranchless<DateTime>(
            source,
            levels,
            dates
        );

        var guidLevels = new int[Count];
        var guidValues = new Guid[Count];
        var presence = new byte[Count];
        int packedGuids = NullableColumnExtractor.ExtractTwoPass<Guid>(
            guids,
            presence,
            guidLevels,
            guidValues
        );

        Assert.Equal(75, packedDates);
        Assert.Equal(75, packedGuids);
        Assert.True(levels.SequenceEqual(guidLevels));
        for (int i = 0, k = 0; i < Count; i++)
        {
            if (i % 4 == 0)
                continue;
            Assert.Equal(source[i]!.Value, dates[k]);
            Assert.Equal(guids[i]!.Value, guidValues[k]);
            k++;
        }
    }

    [Fact]
    public void ShortDestinationsThrow()
    {
        var source = new int?[8];
        Assert.Throws<ArgumentException>(() =>
            NullableColumnExtractor.ExtractBranchless<int>(source, new int[4], new int[8])
        );
        Assert.Throws<ArgumentException>(() =>
            NullableColumnExtractor.ExtractBranchless<int>(source, new int[8], new int[4])
        );
        Assert.Throws<ArgumentException>(() =>
            NullableColumnExtractor.ExtractTwoPass<int>(source, new byte[4], new int[8], new int[8])
        );
        Assert.Throws<ArgumentException>(() =>
            NullableColumnExtractor.ExpandPresenceToDefinitionLevels(new byte[8], new int[4])
        );
        Assert.Throws<ArgumentException>(() =>
            NullableColumnExtractor.CompactInPlace<int>(new int[8], new byte[4])
        );
    }
}
