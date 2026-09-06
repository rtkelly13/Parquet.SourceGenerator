using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Parquet;
using Parquet.Data;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record PayloadSemanticsModel
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("req_str")]
    public string RequiredString { get; init; } = string.Empty;

    [ParquetColumn("opt_str")]
    public string? OptionalString { get; init; }

    [ParquetColumn("req_bytes")]
    public byte[] RequiredBytes { get; init; } = Array.Empty<byte>();

    [ParquetColumn("opt_bytes")]
    public byte[]? OptionalBytes { get; init; }
}

public class NullVsEmptyAndLargePayloadTests
{
    [Fact]
    public async Task ExactSemanticPreservationNullVsEmptyStringAndByteArray()
    {
        var original = new List<PayloadSemanticsModel>
        {
            new()
            {
                Id = 1,
                RequiredString = string.Empty,
                OptionalString = string.Empty,
                RequiredBytes = Array.Empty<byte>(),
                OptionalBytes = Array.Empty<byte>(),
            },
            new()
            {
                Id = 2,
                RequiredString = string.Empty,
                OptionalString = null,
                RequiredBytes = Array.Empty<byte>(),
                OptionalBytes = null,
            },
            new()
            {
                Id = 3,
                RequiredString = "standard text",
                OptionalString = "optional text",
                RequiredBytes = new byte[] { 0x01, 0x02, 0x03 },
                OptionalBytes = new byte[] { 0x04, 0x05, 0x06 },
            },
        };

        using var stream = new MemoryStream();
        await original.WriteParquetAsync(stream);

        stream.Position = 0;
        List<PayloadSemanticsModel> read =
            await PayloadSemanticsModelParquetExtensions.ReadParquetAsync(stream);

        ParquetCompatibilityOracle.AssertEquivalent(original, read);
    }

    [Fact]
    public async Task LowLevelParquetDefinitionLevelsDistinguishNullFromEmpty()
    {
        var items = new List<PayloadSemanticsModel>
        {
            new()
            {
                Id = 1,
                RequiredString = "",
                OptionalString = "",
                RequiredBytes = Array.Empty<byte>(),
                OptionalBytes = Array.Empty<byte>(),
            },
            new()
            {
                Id = 2,
                RequiredString = "",
                OptionalString = null,
                RequiredBytes = Array.Empty<byte>(),
                OptionalBytes = null,
            },
        };

        using var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);

        stream.Position = 0;
        await using var reader = await ParquetReader.CreateAsync(stream);
        using var groupReader = reader.OpenRowGroupReader(0);

        Parquet.Schema.DataField optStrField =
            PayloadSemanticsModelParquetExtensions.Schema.DataFields.Single(f =>
                f.Name == "opt_str"
            );
        string?[] strData = new string?[2];
        await groupReader.ReadAsync(optStrField, strData.AsMemory());

        Assert.Equal(string.Empty, strData[0]);
        Assert.Null(strData[1]);

        Parquet.Schema.DataField optBytesField =
            PayloadSemanticsModelParquetExtensions.Schema.DataFields.Single(f =>
                f.Name == "opt_bytes"
            );
        byte[]?[] bytesData = new byte[]?[2];
        await groupReader.ReadAsync(optBytesField, bytesData.AsMemory());

        Assert.NotNull(bytesData[0]);
        Assert.Empty(bytesData[0]!);
        Assert.Null(bytesData[1]);
    }

    [Fact]
    public async Task WhitespaceAndDiverseUnicodeStringsRoundTripPreserved()
    {
        var testStrings = new[]
        {
            string.Empty,
            " ",
            "    ",
            "\t",
            "\r\n\t \r\n",
            "\u00A0\u2000\u2001\u2009\u3000", // Non-breaking, quad, en-space, thin space, ideographic space
            "👨‍👩‍👧‍👦 👩🏽‍💻 🚀 🔥 ⚡️", // Multi-codepoint emojis with ZWJ and modifiers
            "こんにちは世界 汉语/漢語 한국어", // CJK characters
            "مرحبا بالعالم שלום עולם", // Right-to-left Arabic and Hebrew
            "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿ", // Accented Latin
            "∀x ∈ ℝ: ∃y (x² + y² = 1) ∧ ∑_{i=0}^n x_i ∏ ∯ ∇ × E = -∂B/∂t", // Mathematical symbols
            "Line1\nLine2\r\nLine3\rLine4", // Mixed newlines
            "\"Quoted\" and 'SingleQuoted' and `Backticked` <XML attr=\"val\" />", // Code snippets
        };

        var items = new List<PayloadSemanticsModel>();
        for (int i = 0; i < testStrings.Length; i++)
        {
            items.Add(
                new PayloadSemanticsModel
                {
                    Id = i,
                    RequiredString = testStrings[i],
                    OptionalString = i % 2 == 0 ? testStrings[i] : null,
                    RequiredBytes = Encoding.UTF8.GetBytes(testStrings[i]),
                    OptionalBytes = i % 2 == 0 ? Encoding.UTF8.GetBytes(testStrings[i]) : null,
                }
            );
        }

        using var stream = new MemoryStream();
        await items.WriteParquetAsync(stream);

        stream.Position = 0;
        List<PayloadSemanticsModel> results =
            await PayloadSemanticsModelParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(items.Count, results.Count);

        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(items[i].Id, results[i].Id);
            Assert.Equal(items[i].RequiredString, results[i].RequiredString);
            Assert.Equal(items[i].OptionalString, results[i].OptionalString);
            Assert.Equal(items[i].RequiredBytes, results[i].RequiredBytes);
            Assert.Equal(items[i].OptionalBytes, results[i].OptionalBytes);
        }
    }

    [Fact]
    public async Task LargePayloadStressTestStringsAndByteArraysAcrossMultipleRowGroups()
    {
        // Generate strings > 100 KB and byte arrays > 256 KB
        const int stringSize1 = 120 * 1024; // 120 KB
        const int stringSize2 = 160 * 1024; // 160 KB
        const int binarySize1 = 300 * 1024; // 300 KB
        const int binarySize2 = 512 * 1024; // 512 KB

        string largeString1 = GenerateDeterministicString(stringSize1, seed: 101);
        string largeString2 = GenerateDeterministicString(stringSize2, seed: 202);

        byte[] largeBytes1 = new byte[binarySize1];
        byte[] largeBytes2 = new byte[binarySize2];
        new Random(42).NextBytes(largeBytes1);
        new Random(99).NextBytes(largeBytes2);

        var items = new List<PayloadSemanticsModel>
        {
            new()
            {
                Id = 1,
                RequiredString = largeString1,
                OptionalString = largeString2,
                RequiredBytes = largeBytes1,
                OptionalBytes = largeBytes2,
            },
            new()
            {
                Id = 2,
                RequiredString = string.Empty,
                OptionalString = null,
                RequiredBytes = Array.Empty<byte>(),
                OptionalBytes = null,
            },
            new()
            {
                Id = 3,
                RequiredString = "interleaved small payload",
                OptionalString = string.Empty,
                RequiredBytes = new byte[] { 42 },
                OptionalBytes = Array.Empty<byte>(),
            },
            new()
            {
                Id = 4,
                RequiredString = largeString2,
                OptionalString = null,
                RequiredBytes = largeBytes2,
                OptionalBytes = largeBytes1,
            },
        };

        // Write with RowGroupSize = 2 to force multiple row groups and buffer recycling
        var options = new ParquetSerializerOptions { RowGroupSize = 2 };

        using var stream = new MemoryStream();
        await items.WriteParquetAsync(stream, options);

        // 1. Sequential Read
        stream.Position = 0;
        List<PayloadSemanticsModel> sequentialResults =
            await PayloadSemanticsModelParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(items.Count, sequentialResults.Count);
        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(items[i].Id, sequentialResults[i].Id);
            Assert.Equal(items[i].RequiredString, sequentialResults[i].RequiredString);
            Assert.Equal(items[i].OptionalString, sequentialResults[i].OptionalString);
            Assert.Equal(items[i].RequiredBytes, sequentialResults[i].RequiredBytes);
            Assert.Equal(items[i].OptionalBytes, sequentialResults[i].OptionalBytes);
        }

        // 2. Parallel Read
        stream.Position = 0;
        List<PayloadSemanticsModel> parallelResults =
            await PayloadSemanticsModelParquetExtensions.ReadParquetParallelAsync(stream);

        Assert.Equal(items.Count, parallelResults.Count);
        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(items[i].Id, parallelResults[i].Id);
            Assert.Equal(items[i].RequiredString, parallelResults[i].RequiredString);
            Assert.Equal(items[i].OptionalString, parallelResults[i].OptionalString);
            Assert.Equal(items[i].RequiredBytes, parallelResults[i].RequiredBytes);
            Assert.Equal(items[i].OptionalBytes, parallelResults[i].OptionalBytes);
        }
    }

    private static string GenerateDeterministicString(int charCount, int seed)
    {
        var rng = new Random(seed);
        const string alphabet =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()_+-=[]{}|;:,.<>?/ \n\t";
        var sb = new StringBuilder(charCount);
        for (int i = 0; i < charCount; i++)
        {
            sb.Append(alphabet[rng.Next(alphabet.Length)]);
        }
        return sb.ToString();
    }
}
