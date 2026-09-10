using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// A model whose string columns exercise the raw UTF-16 span read path introduced for
/// issue #143 (span-keyed string deduplication).
/// </summary>
[ParquetSerializable]
public partial record SpanDedupRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    /// <summary>Non-nullable: read with no definition-level lane.</summary>
    [ParquetColumn("required_category")]
    public string RequiredCategory { get; init; } = string.Empty;

    /// <summary>Nullable: read with a definition-level lane and a packed value lane.</summary>
    [ParquetColumn("optional_category")]
    public string? OptionalCategory { get; init; }
}

/// <summary>
/// Covers the deduplicating raw-span read path: value fidelity for awkward payloads,
/// null/empty discrimination, hash-collision safety and the allocation claim.
/// </summary>
public sealed class SpanStringDeduplicationTests
{
    private static readonly ParquetSerializerOptions Deduplicating = new()
    {
        DeduplicateStrings = true,
    };

    private static readonly ParquetSerializerOptions NotDeduplicating = new()
    {
        DeduplicateStrings = false,
    };

    private static async Task<byte[]> WriteAsync(List<SpanDedupRecord> items)
    {
        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);
        return ms.ToArray();
    }

    private static async Task AssertRoundTripsAsync(List<SpanDedupRecord> items)
    {
        byte[] bytes = await WriteAsync(items);

        var deduplicated = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            Deduplicating
        );
        var plain = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            NotDeduplicating
        );

        Assert.Equal(items.Count, deduplicated.Count);
        Assert.Equal(items.Count, plain.Count);

        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(items[i].RequiredCategory, deduplicated[i].RequiredCategory);
            Assert.Equal(items[i].OptionalCategory, deduplicated[i].OptionalCategory);

            // The deduplicating path must agree with the plain path value for value.
            Assert.Equal(plain[i].RequiredCategory, deduplicated[i].RequiredCategory);
            Assert.Equal(plain[i].OptionalCategory, deduplicated[i].OptionalCategory);
        }
    }

    [Fact]
    public async Task EmptyAndNullValuesStayDistinctThroughTheSpanPath()
    {
        var items = new List<SpanDedupRecord>();
        for (int i = 0; i < 64; i++)
        {
            items.Add(
                new SpanDedupRecord
                {
                    Id = i,
                    RequiredCategory = (i % 3) switch
                    {
                        0 => string.Empty,
                        1 => "Alpha",
                        _ => "Beta",
                    },
                    OptionalCategory = (i % 4) switch
                    {
                        0 => null,
                        1 => string.Empty,
                        2 => "Alpha",
                        _ => "Beta",
                    },
                }
            );
        }

        await AssertRoundTripsAsync(items);

        byte[] bytes = await WriteAsync(items);
        var read = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            Deduplicating
        );

        // An empty string must never be conflated with a null.
        Assert.Null(read[0].OptionalCategory);
        Assert.Equal(string.Empty, read[1].OptionalCategory);
    }

    [Fact]
    public async Task EmbeddedNulCharactersSurviveTheSpanPath()
    {
        string withNul = "left\0right";
        string onlyNuls = "\0\0\0";
        var items = new List<SpanDedupRecord>();
        for (int i = 0; i < 48; i++)
        {
            items.Add(
                new SpanDedupRecord
                {
                    Id = i,
                    RequiredCategory = (i % 2 == 0) ? withNul : onlyNuls,
                    OptionalCategory = (i % 3 == 0) ? withNul : null,
                }
            );
        }

        await AssertRoundTripsAsync(items);

        byte[] bytes = await WriteAsync(items);
        var read = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            Deduplicating
        );

        // A NUL must terminate nothing: full length has to be preserved.
        Assert.Equal(withNul.Length, read[0].RequiredCategory.Length);
        Assert.Equal(withNul, read[0].RequiredCategory);
        Assert.Equal(onlyNuls, read[1].RequiredCategory);
    }

    [Fact]
    public async Task VeryLongAndNonBmpValuesSurviveTheSpanPath()
    {
        string veryLong = new string('x', 200_000) + "|tail";
        string nonBmp = "🎿🧪𝔘𝔫𝔦𝔠𝔬𝔡𝔢 — ünïcodé";
        string almostVeryLong = new string('x', 200_000) + "|tale"; // differs only at the end

        var items = new List<SpanDedupRecord>
        {
            new()
            {
                Id = 0,
                RequiredCategory = veryLong,
                OptionalCategory = nonBmp,
            },
            new()
            {
                Id = 1,
                RequiredCategory = almostVeryLong,
                OptionalCategory = null,
            },
            new()
            {
                Id = 2,
                RequiredCategory = veryLong,
                OptionalCategory = nonBmp,
            },
        };

        await AssertRoundTripsAsync(items);

        byte[] bytes = await WriteAsync(items);
        var read = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            Deduplicating
        );

        // Two values sharing a 200,000-character prefix must not be conflated.
        Assert.Equal(veryLong, read[0].RequiredCategory);
        Assert.Equal(almostVeryLong, read[1].RequiredCategory);
        Assert.NotEqual(read[0].RequiredCategory, read[1].RequiredCategory);

        // The repeated long value should still be deduplicated to a single instance.
        Assert.Same(read[0].RequiredCategory, read[2].RequiredCategory);
    }

    [Fact]
    public async Task HashCollisionsFallBackToFullComparison()
    {
        // Build a set of distinct values that all land in the same 512-slot bucket under the
        // emitted FNV-1a hash, so every read after the first is a collision.
        const int TableMask = 511;
        int targetBucket = -1;
        var colliding = new List<string>();
        for (int i = 0; colliding.Count < 40 && i < 2_000_000; i++)
        {
            string candidate =
                "collide-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int bucket = (int)(Fnv1a(candidate) & TableMask);
            if (targetBucket < 0)
            {
                targetBucket = bucket;
            }

            if (bucket == targetBucket)
            {
                colliding.Add(candidate);
            }
        }

        Assert.Equal(40, colliding.Count);

        var items = new List<SpanDedupRecord>();
        for (int i = 0; i < 400; i++)
        {
            string value = colliding[i % colliding.Count];
            items.Add(
                new SpanDedupRecord
                {
                    Id = i,
                    RequiredCategory = value,
                    OptionalCategory = value,
                }
            );
        }

        // Every row must come back with its own value even though all 40 distinct values
        // hash into a single bucket — the cache must never return a same-hash neighbour.
        await AssertRoundTripsAsync(items);
    }

    [Fact]
    public async Task RepeatedValuesShareASingleInstance()
    {
        var items = new List<SpanDedupRecord>();
        for (int i = 0; i < 500; i++)
        {
            items.Add(
                new SpanDedupRecord
                {
                    Id = i,
                    RequiredCategory = (i % 2 == 0) ? "Electronics" : "Clothing",
                    OptionalCategory = (i % 3 == 0) ? "OnSale" : null,
                }
            );
        }

        byte[] bytes = await WriteAsync(items);
        var read = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
            new MemoryStream(bytes),
            Deduplicating
        );

        var distinctRequired = read.Select(r => r.RequiredCategory)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count();
        Assert.Equal(2, distinctRequired);

        var distinctOptional = read.Where(r => r.OptionalCategory is not null)
            .Select(r => (object)r.OptionalCategory!)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count();
        Assert.Equal(1, distinctOptional);
    }

    [Fact]
    public async Task DeduplicatedReadAllocatesLessThanPlainRead()
    {
        var items = new List<SpanDedupRecord>();
        for (int i = 0; i < 20_000; i++)
        {
            items.Add(
                new SpanDedupRecord
                {
                    Id = i,
                    RequiredCategory =
                        "Category-"
                        + (i % 8).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    OptionalCategory =
                        "Tag-"
                        + (i % 5).ToString(System.Globalization.CultureInfo.InvariantCulture),
                }
            );
        }

        byte[] bytes = await WriteAsync(items);

        async Task<long> MeasureAsync(ParquetSerializerOptions options)
        {
            // Warm the code paths first so JIT allocations do not pollute the measurement.
            _ = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
                new MemoryStream(bytes),
                options
            );
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            var read = await SpanDedupRecordParquetExtensions.ReadParquetAsync(
                new MemoryStream(bytes),
                options
            );
            long after = GC.GetTotalAllocatedBytes(precise: true);
            Assert.Equal(items.Count, read.Count);
            return after - before;
        }

        long plain = await MeasureAsync(NotDeduplicating);
        long deduplicated = await MeasureAsync(Deduplicating);

        Assert.True(
            deduplicated < plain,
            $"Deduplicated read allocated {deduplicated:N0} bytes, plain read allocated {plain:N0} bytes."
        );
    }

    /// <summary>
    /// Mirrors the FNV-1a hash emitted into <c>StringDeduplicator</c> so tests can construct
    /// deliberate bucket collisions.
    /// </summary>
    private static uint Fnv1a(string value)
    {
        uint hash = 2166136261u;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 16777619u;
        }

        return hash;
    }
}
