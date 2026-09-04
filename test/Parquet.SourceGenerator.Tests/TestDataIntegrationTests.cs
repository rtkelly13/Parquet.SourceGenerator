using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record TestUserRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string Name { get; init; } = string.Empty;

    [ParquetColumn("score")]
    public double Score { get; init; }

    [ParquetColumn("is_active")]
    public bool IsActive { get; init; }

    [ParquetColumn("created_at_ms")]
    public long CreatedAtMs { get; init; }
}

[ParquetSerializable]
public partial record TestNullableRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("nullable_int")]
    public int? NullableInt { get; init; }

    [ParquetColumn("nullable_double")]
    public double? NullableDouble { get; init; }

    [ParquetColumn("nullable_string")]
    public string? NullableString { get; init; }

    [ParquetColumn("nullable_bool")]
    public bool? NullableBool { get; init; }
}

[ParquetSerializable]
public partial record TestLargeFlatRecord
{
    [ParquetColumn("id")]
    public long Id { get; init; }

    [ParquetColumn("payload")]
    public string Payload { get; init; } = string.Empty;

    [ParquetColumn("val_a")]
    public int ValA { get; init; }

    [ParquetColumn("val_b")]
    public double ValB { get; init; }

    [ParquetColumn("is_valid")]
    public bool IsValid { get; init; }
}

public sealed class TestDataIntegrationTests
{
    private static readonly string TestDataRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data")
    );
    private static readonly string TestDataCSharpRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data_csharp")
    );

    [Fact]
    public async Task ReadParquetAsyncDeserializesPyArrowV1Dataset()
    {
        string filePath = Path.Combine(TestDataRoot, "v1", "01_small_flat_primitives.parquet");
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        using var stream = System.IO.File.OpenRead(filePath);
        var records = await TestUserRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(100, records.Count);
        Assert.Equal(0, records[0].Id);
        Assert.Equal("user_0", records[0].Name);
        Assert.Equal(0.0, records[0].Score);
        Assert.True(records[0].IsActive);
        Assert.Equal(1700000000000L, records[0].CreatedAtMs);

        Assert.Equal(99, records[99].Id);
        Assert.Equal("user_99", records[99].Name);
        Assert.False(records[99].IsActive);
    }

    [Fact]
    public async Task ReadParquetAsyncDeserializesPyArrowV2Dataset()
    {
        string filePath = Path.Combine(TestDataRoot, "v2", "01_small_flat_primitives.parquet");
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        using var stream = System.IO.File.OpenRead(filePath);
        var records = await TestUserRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(100, records.Count);
        Assert.Equal(50, records[50].Id);
        Assert.Equal("user_50", records[50].Name);
        Assert.True(records[50].IsActive);
    }

    [Fact]
    public async Task ReadParquetAsyncDeserializesCSharpV3Dataset()
    {
        string filePath = Path.Combine(
            TestDataCSharpRoot,
            "v3",
            "01_small_flat_primitives.parquet"
        );
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        using var stream = System.IO.File.OpenRead(filePath);
        var records = await TestUserRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(100, records.Count);
        Assert.Equal(10, records[10].Id);
        Assert.Equal("user_10", records[10].Name);
        Assert.True(records[10].IsActive);
    }

    [Fact]
    public async Task ReadParquetAsyncDeserializesNullableDataset()
    {
        string filePath = Path.Combine(
            TestDataCSharpRoot,
            "v3",
            "02_medium_nullable_types.parquet"
        );
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        using var stream = System.IO.File.OpenRead(filePath);
        var records = await TestNullableRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(10000, records.Count);
        Assert.Null(records[0].NullableInt);
        Assert.Null(records[0].NullableString);

        Assert.Equal(10, records[1].NullableInt);
        Assert.Equal("str_val_1", records[1].NullableString);
    }

    [Fact]
    public async Task ReadParquetAsyncDeserializesLargeScaleDataset()
    {
        string filePath = Path.Combine(TestDataCSharpRoot, "v3", "05_large_scale_flat.parquet");
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        using var stream = System.IO.File.OpenRead(filePath);
        var records = await TestLargeFlatRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(100000, records.Count);
        Assert.Equal(0L, records[0].Id);
        Assert.Equal(99999L, records[99999].Id);
        Assert.Equal(699993, records[99999].ValA);
    }
}
