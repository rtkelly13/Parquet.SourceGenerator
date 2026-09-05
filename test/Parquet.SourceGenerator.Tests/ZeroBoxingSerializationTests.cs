using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Parquet.SourceGenerator;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record ZeroBoxingRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string Name { get; init; } = string.Empty;

    [ParquetColumn("description")]
    public string? Description { get; init; }

    [ParquetColumn("data")]
    public byte[] Data { get; init; } = Array.Empty<byte>();

    [ParquetColumn("optional_data")]
    public byte[]? OptionalData { get; init; }
}

public class ZeroBoxingSerializationTests
{
    [Fact]
    public void WriteParquetRowGroupAsyncEmitsZeroBoxingOpcodes()
    {
        MethodInfo? method = typeof(ZeroBoxingRecordParquetExtensions).GetMethod(
            "WriteParquetRowGroupAsync",
            BindingFlags.Public | BindingFlags.Static
        );

        Assert.NotNull(method);
        MethodBody? body = method.GetMethodBody();
        Assert.NotNull(body);

        byte[] il = body.GetILAsByteArray()!;
        byte boxOpcode = (byte)OpCodes.Box.Value;

        int boxCount = il.Count(b => b == boxOpcode);
        Assert.Equal(0, boxCount);
    }

    [Fact]
    public async Task RoundTripZeroBoxingStringAndBinaryDataPreservesAllValues()
    {
        var items = new List<ZeroBoxingRecord>
        {
            new ZeroBoxingRecord
            {
                Id = 1,
                Name = "Alpha",
                Description = "Standard string",
                Data = new byte[] { 1, 2, 3, 4 },
                OptionalData = new byte[] { 5, 6, 7, 8 },
            },
            new ZeroBoxingRecord
            {
                Id = 2,
                Name = "Beta",
                Description = null, // Nullable string = null
                Data = Array.Empty<byte>(),
                OptionalData = null, // Nullable byte array = null
            },
            new ZeroBoxingRecord
            {
                Id = 3,
                Name = "Gamma 🚀 Unicode & 汉语",
                Description = "Complex Unicode with emojis ⚡️🔥",
                Data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                OptionalData = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE },
            },
            new ZeroBoxingRecord
            {
                Id = 4,
                Name = string.Empty,
                Description = string.Empty,
                Data = new byte[] { 0 },
                OptionalData = new byte[] { 0 },
            },
        };

        using var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);

        stream.Position = 0;
        List<ZeroBoxingRecord> results = await ZeroBoxingRecordParquetExtensions.ReadParquetAsync(
            stream
        );

        Assert.Equal(items.Count, results.Count);

        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(items[i].Id, results[i].Id);
            Assert.Equal(items[i].Name, results[i].Name);
            Assert.Equal(items[i].Description, results[i].Description);
            Assert.Equal(items[i].Data, results[i].Data);
            Assert.Equal(items[i].OptionalData, results[i].OptionalData);
        }
    }

    [Fact]
    public async Task RoundTripLargeBatchRecyclesBuffersWithoutCorruption()
    {
        const int count = 2_000;
        var items = Enumerable
            .Range(0, count)
            .Select(i => new ZeroBoxingRecord
            {
                Id = i,
                Name = $"Name_{i % 50}",
                Description = i % 3 == 0 ? null : $"Description_{i}",
                Data = new byte[] { (byte)(i & 0xFF), (byte)((i >> 8) & 0xFF) },
                OptionalData = i % 2 == 0 ? null : new byte[] { (byte)(i % 100) },
            })
            .ToList();

        using var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);

        stream.Position = 0;
        List<ZeroBoxingRecord> results = await ZeroBoxingRecordParquetExtensions.ReadParquetAsync(
            stream
        );

        Assert.Equal(count, results.Count);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(items[i].Id, results[i].Id);
            Assert.Equal(items[i].Name, results[i].Name);
            Assert.Equal(items[i].Description, results[i].Description);
            Assert.Equal(items[i].Data, results[i].Data);
            Assert.Equal(items[i].OptionalData, results[i].OptionalData);
        }
    }
}
