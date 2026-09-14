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
}
