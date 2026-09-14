using System;
using Bogus;

namespace Parquet.SourceGenerator.Tests.Fixtures;

/// <summary>
/// Deterministic, reproducible Bogus fakers for integration test records.
/// Default seeds guarantee deterministic output across test runs, machine environments, and CI.
/// </summary>
public static class TestFakers
{
    public const int DefaultSeed = 42;

    /// <summary>
    /// Creates a seeded <see cref="Faker{TypeCoverageRecord}"/> generating realistic, varied primitives.
    /// </summary>
    public static Faker<TypeCoverageRecord> CreateTypeCoverageRecordFaker(int seed = DefaultSeed) =>
        new Faker<TypeCoverageRecord>()
            .UseSeed(seed)
            .RuleFor(r => r.Id, f => f.IndexFaker + 1)
            .RuleFor(
                r => r.CreatedAt,
                f => f.Date.Recent(30, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            )
            .RuleFor(r => r.CorrelationId, f => f.Random.Guid())
            .RuleFor(r => r.Status, f => f.PickRandom<EventStatus>())
            .RuleFor(r => r.Duration, f => TimeSpan.FromMilliseconds(f.Random.Int(10, 60_000)));

    /// <summary>
    /// Creates a seeded <see cref="Faker{NullableTypeCoverageRecord}"/> with controlled nullability.
    /// </summary>
    public static Faker<NullableTypeCoverageRecord> CreateNullableTypeCoverageRecordFaker(
        int seed = DefaultSeed,
        float nullWeight = 0.2f
    ) =>
        new Faker<NullableTypeCoverageRecord>()
            .UseSeed(seed)
            .RuleFor(r => r.Id, f => f.IndexFaker + 1)
            .RuleFor(
                r => r.CreatedAt,
                f =>
                    f.Random.Bool(nullWeight)
                        ? null
                        : f.Date.Recent(30, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            )
            .RuleFor(r => r.CorrelationId, f => f.Random.Bool(nullWeight) ? null : f.Random.Guid())
            .RuleFor(
                r => r.Status,
                f => f.Random.Bool(nullWeight) ? null : f.PickRandom<EventStatus>()
            );

    /// <summary>
    /// Creates a seeded <see cref="Faker{JsonAnnotatedModel}"/> generating diverse names and scores.
    /// </summary>
    public static Faker<JsonAnnotatedModel> CreateJsonAnnotatedModelFaker(int seed = DefaultSeed) =>
        new Faker<JsonAnnotatedModel>()
            .UseSeed(seed)
            .RuleFor(m => m.Id, f => f.IndexFaker + 1)
            .RuleFor(m => m.Name, f => f.Commerce.ProductName())
            .RuleFor(m => m.Score, f => f.Random.Double(0.0, 100.0))
            .RuleFor(m => m.InternalSecret, _ => "hidden");

    /// <summary>
    /// Creates a seeded <see cref="Faker{Address}"/> generating cities and zip codes.
    /// </summary>
    public static Faker<Address> CreateAddressFaker(int seed = DefaultSeed) =>
        new Faker<Address>()
            .UseSeed(seed)
            .RuleFor(a => a.City, f => f.Address.City())
            .RuleFor(a => a.Zip, f => f.Random.Int(10000, 99999));

    /// <summary>
    /// Creates a seeded <see cref="Faker{NestedOrder}"/> generating nested order records.
    /// </summary>
    public static Faker<NestedOrder> CreateNestedOrderFaker(
        int seed = DefaultSeed,
        float nullShipWeight = 0.2f
    )
    {
        var addressFaker = CreateAddressFaker(seed);
        return new Faker<NestedOrder>()
            .UseSeed(seed)
            .RuleFor(o => o.Id, f => f.IndexFaker + 1)
            .RuleFor(
                o => o.Ship,
                f => f.Random.Bool(nullShipWeight) ? null : addressFaker.Generate()
            )
            .RuleFor(o => o.Bill, _ => addressFaker.Generate());
    }

    /// <summary>
    /// Creates a seeded <see cref="Faker{ZeroBoxingRecord}"/> generating varied test records.
    /// </summary>
    public static Faker<ZeroBoxingRecord> CreateZeroBoxingRecordFaker(int seed = DefaultSeed) =>
        new Faker<ZeroBoxingRecord>()
            .UseSeed(seed)
            .RuleFor(r => r.Id, f => f.IndexFaker + 1)
            .RuleFor(r => r.Name, f => f.Commerce.ProductName())
            .RuleFor(r => r.Description, f => f.Random.Bool(0.2f) ? null : f.Lorem.Sentence())
            .RuleFor(r => r.Data, f => f.Random.Bytes(f.Random.Int(4, 32)))
            .RuleFor(r => r.OptionalData, f => f.Random.Bool(0.3f) ? null : f.Random.Bytes(8));

    /// <summary>
    /// Creates a seeded <see cref="Faker{SpanDedupRecord}"/> generating string deduplication records.
    /// </summary>
    public static Faker<SpanDedupRecord> CreateSpanDedupRecordFaker(
        int seed = DefaultSeed,
        string[]? categories = null
    )
    {
        string[] cats = categories ?? ["alpha", "beta", "gamma", "delta"];
        return new Faker<SpanDedupRecord>()
            .UseSeed(seed)
            .RuleFor(r => r.Id, f => f.IndexFaker + 1)
            .RuleFor(r => r.RequiredCategory, f => f.PickRandom(cats))
            .RuleFor(r => r.OptionalCategory, f => f.Random.Bool(0.3f) ? null : f.PickRandom(cats));
    }
}
