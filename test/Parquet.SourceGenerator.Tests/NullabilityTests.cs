using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record NullabilityModel
{
    [ParquetColumn("required_id")]
    public int RequiredId { get; init; }

    [ParquetColumn("optional_id")]
    public int? OptionalId { get; init; }

    [ParquetColumn("required_name")]
    public string RequiredName { get; init; } = string.Empty;

    [ParquetColumn("optional_name")]
    public string? OptionalName { get; init; }

    [ParquetColumn("required_payload")]
    public byte[] RequiredPayload { get; init; } = Array.Empty<byte>();

    [ParquetColumn("optional_payload")]
    public byte[]? OptionalPayload { get; init; }
}

/// <summary>
/// Nullable reference annotations now drive the column's optionality. Previously every reference
/// type produced an optional column, so <c>string</c> and <c>string?</c> were indistinguishable in
/// the written schema.
/// </summary>
public sealed class NullabilityTests
{
    private static bool IsColumnNullable(string columnName) =>
        NullabilityModelParquetExtensions
            .Schema.DataFields.Single(f => f.Name == columnName)
            .IsNullable;

    [Theory]
    [InlineData("required_id")]
    [InlineData("required_name")]
    [InlineData("required_payload")]
    public void NonNullableMembersProduceRequiredColumns(string columnName)
    {
        Assert.False(IsColumnNullable(columnName), $"'{columnName}' should be a required column");
    }

    [Theory]
    [InlineData("optional_id")]
    [InlineData("optional_name")]
    [InlineData("optional_payload")]
    public void NullableMembersProduceOptionalColumns(string columnName)
    {
        Assert.True(IsColumnNullable(columnName), $"'{columnName}' should be an optional column");
    }

    [Fact]
    public async Task NullValuesStillRoundTripThroughOptionalColumns()
    {
        var written = new List<NullabilityModel>
        {
            new()
            {
                RequiredId = 1,
                OptionalId = null,
                RequiredName = "present",
                OptionalName = null,
                RequiredPayload = new byte[] { 1, 2 },
                OptionalPayload = null,
            },
            new()
            {
                RequiredId = 2,
                OptionalId = 42,
                RequiredName = "also present",
                OptionalName = "set",
                RequiredPayload = new byte[] { 3 },
                OptionalPayload = new byte[] { 4, 5 },
            },
        };

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        stream.Position = 0;

        List<NullabilityModel> read = await NullabilityModelParquetExtensions.ReadParquetAsync(
            stream
        );

        Assert.Equal(2, read.Count);
        Assert.Null(read[0].OptionalId);
        Assert.Null(read[0].OptionalName);
        Assert.Null(read[0].OptionalPayload);
        Assert.Equal(42, read[1].OptionalId);
        Assert.Equal("set", read[1].OptionalName);
        Assert.Equal(new byte[] { 4, 5 }, read[1].OptionalPayload);
        Assert.Equal("present", read[0].RequiredName);
    }
}
