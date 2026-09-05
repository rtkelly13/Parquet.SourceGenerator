using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Cryptographic hash-based regression suite for Parquet generation and dataset integrity.
/// Guarantees:
/// 1. Source-generated serialization (WriteParquetAsync, WriteParquetBatchedAsync) produces
///    strictly bit-for-bit deterministic output across repeated executions.
/// 2. Serialized output for canonical models matches pinned golden SHA-256 hashes, catching any
///    unintended codec, dictionary, schema, or structural binary changes.
/// 3. All tracked repository Parquet datasets (PyArrow v1/v2, C# v3, and LFS benchmarks) maintain
///    cryptographic SHA-256 hash integrity.
/// </summary>
public sealed class ParquetHashRegressionTests
{
    private static readonly string SolutionRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
    );

    private static readonly string TestDataRoot = Path.Combine(SolutionRoot, "test", "data");
    private static readonly string TestDataCSharpRoot = Path.Combine(
        SolutionRoot,
        "test",
        "data_csharp"
    );
    private static readonly string BenchmarkDataRoot = Path.Combine(
        SolutionRoot,
        "benchmarks",
        "data"
    );

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256(Stream stream)
    {
        stream.Position = 0;
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static List<TestUserRecord> CreateDeterministicUserRecords(int count = 100)
    {
        return Enumerable
            .Range(0, count)
            .Select(i => new TestUserRecord
            {
                Id = i,
                Name = $"user_{i}",
                Score = (i * 1.5) % 100.0,
                IsActive = i % 2 == 0,
                CreatedAtMs = 1700000000000L + (i * 1000L),
            })
            .ToList();
    }

    private static List<TestNullableRecord> CreateDeterministicNullableRecords(int count = 1000)
    {
        return Enumerable
            .Range(0, count)
            .Select(i => new TestNullableRecord
            {
                Id = i,
                NullableInt = i % 5 == 0 ? null : i * 10,
                NullableDouble = i % 5 == 0 ? null : (i * 3.14159) % 1000.0,
                NullableString = i % 5 == 0 ? null : $"str_val_{i}",
                NullableBool = i % 5 == 0 ? null : (i % 3 == 0),
            })
            .ToList();
    }

    private static List<BenchmarkGuidModel> CreateDeterministicGuidRecords(int count = 500)
    {
        return Enumerable
            .Range(0, count)
            .Select(i => new BenchmarkGuidModel
            {
                Id = i,
                CorrelationId = new Guid(i + 1, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8),
                Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
            })
            .ToList();
    }

    // =========================================================================
    // 1. In-Memory Determinism Tests (Bit-for-Bit Identity Across Repeated Runs)
    // =========================================================================

    [Theory]
    [InlineData(ParquetCompressionMethod.None)]
    [InlineData(ParquetCompressionMethod.Snappy)]
    [InlineData(ParquetCompressionMethod.Gzip)]
    [InlineData(ParquetCompressionMethod.Zstd)]
    public async Task WriteParquetAsyncIsBitForBitDeterministicAcrossCodecs(
        ParquetCompressionMethod compressionMethod
    )
    {
        var records = CreateDeterministicUserRecords(100);
        var options = new ParquetSerializerOptions { CompressionMethod = compressionMethod };

        using var stream1 = new MemoryStream();
        await records.WriteParquetAsync(stream1, options: options);
        byte[] bytes1 = stream1.ToArray();

        using var stream2 = new MemoryStream();
        await records.WriteParquetAsync(stream2, options: options);
        byte[] bytes2 = stream2.ToArray();

        string hash1 = ComputeSha256(bytes1);
        string hash2 = ComputeSha256(bytes2);

        Assert.Equal(hash1, hash2);
        Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task WriteParquetBatchedAsyncIsBitForBitDeterministicAcrossRowGroupSizes(
        int rowGroupSize
    )
    {
        var records = CreateDeterministicUserRecords(100);

        using var stream1 = new MemoryStream();
        await records.WriteParquetBatchedAsync(stream1, rowGroupSize: rowGroupSize);
        byte[] bytes1 = stream1.ToArray();

        using var stream2 = new MemoryStream();
        await records.WriteParquetBatchedAsync(stream2, rowGroupSize: rowGroupSize);
        byte[] bytes2 = stream2.ToArray();

        string hash1 = ComputeSha256(bytes1);
        string hash2 = ComputeSha256(bytes2);

        Assert.Equal(hash1, hash2);
        Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    [Fact]
    public async Task NullableModelWriteParquetAsyncIsBitForBitDeterministic()
    {
        var records = CreateDeterministicNullableRecords(1000);

        using var stream1 = new MemoryStream();
        await records.WriteParquetAsync(stream1);
        byte[] bytes1 = stream1.ToArray();

        using var stream2 = new MemoryStream();
        await records.WriteParquetAsync(stream2);
        byte[] bytes2 = stream2.ToArray();

        string hash1 = ComputeSha256(bytes1);
        string hash2 = ComputeSha256(bytes2);

        Assert.Equal(hash1, hash2);
        Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    [Fact]
    public async Task GuidModelWriteParquetAsyncIsBitForBitDeterministic()
    {
        var records = CreateDeterministicGuidRecords(500);

        using var stream1 = new MemoryStream();
        await records.WriteParquetAsync(stream1);
        byte[] bytes1 = stream1.ToArray();

        using var stream2 = new MemoryStream();
        await records.WriteParquetAsync(stream2);
        byte[] bytes2 = stream2.ToArray();

        string hash1 = ComputeSha256(bytes1);
        string hash2 = ComputeSha256(bytes2);

        Assert.Equal(hash1, hash2);
        Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
    }

    // =========================================================================
    // 2. Golden SHA-256 Hash Regression Tests (Source Generator Output)
    // =========================================================================

    [Fact]
    public async Task UserRecordsWriteParquetAsyncMatchesGoldenHashSnappy()
    {
        var records = CreateDeterministicUserRecords(100);
        using var stream = new MemoryStream();
        await records.WriteParquetAsync(
            stream,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.Snappy,
            }
        );

        string actualHash = ComputeSha256(stream);
        Assert.Equal(
            "042c17fd3f6b782acf0eaf1d5b7fc82be239ad134695720f3921d72cd8a02518",
            actualHash
        );
    }

    [Fact]
    public async Task UserRecordsWriteParquetAsyncMatchesGoldenHashUncompressed()
    {
        var records = CreateDeterministicUserRecords(100);
        using var stream = new MemoryStream();
        await records.WriteParquetAsync(
            stream,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.None,
            }
        );

        string actualHash = ComputeSha256(stream);
        Assert.Equal(
            "5913c404a436b0c8502db61bbc2f201838ea13a5dc2c055718064129350b8b62",
            actualHash
        );
    }

    [Fact]
    public async Task UserRecordsWriteParquetBatchedAsyncMatchesGoldenHash()
    {
        var records = CreateDeterministicUserRecords(100);
        using var stream = new MemoryStream();
        await records.WriteParquetBatchedAsync(stream, rowGroupSize: 25);

        string actualHash = ComputeSha256(stream);
        Assert.Equal(
            "02738967a4061950a3881e82d2c3db63bdc1949dade0cbafead52347c2069af2",
            actualHash
        );
    }

    [Fact]
    public async Task NullableRecordsWriteParquetAsyncMatchesGoldenHash()
    {
        var records = CreateDeterministicNullableRecords(1000);
        using var stream = new MemoryStream();
        await records.WriteParquetAsync(stream);

        string actualHash = ComputeSha256(stream);
        Assert.Equal(
            "0dc304733a62f1ee46bedb8a2bca6afffd0598a3412cb14d0692ccb4a26f97a0",
            actualHash
        );
    }

    [Fact]
    public async Task GuidRecordsWriteParquetAsyncMatchesGoldenHash()
    {
        var records = CreateDeterministicGuidRecords(500);
        using var stream = new MemoryStream();
        await records.WriteParquetAsync(stream);

        string actualHash = ComputeSha256(stream);
        Assert.Equal(
            "78baabb5a2c8fc5334565d99574af8d550306c3ffa266cd1c882ecb88d91c3a2",
            actualHash
        );
    }

    [Fact]
    public async Task TpchLineItemSerializationMatchesGoldenHash()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "tpch_lineitem_sf001.parquet");
        await using var readStream = System.IO.File.OpenRead(filePath);
        var allRecords = await TpchLineItemRecordParquetExtensions.ReadParquetAsync(readStream);
        var slice = allRecords.Take(200).ToList();

        using var stream1 = new MemoryStream();
        await slice.WriteParquetAsync(
            stream1,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.Snappy,
            }
        );
        byte[] bytes1 = stream1.ToArray();

        using var stream2 = new MemoryStream();
        await slice.WriteParquetAsync(
            stream2,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.Snappy,
            }
        );
        byte[] bytes2 = stream2.ToArray();

        string hash1 = ComputeSha256(bytes1);
        string hash2 = ComputeSha256(bytes2);

        Assert.Equal(hash1, hash2);
        Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
        Assert.Equal("5dd4a14e2b8ceaeed711a514e112f6cfcf3dffc36edf8698afb0cfc6db125b5b", hash1);
    }

    [Fact]
    public async Task AdultCensusSerializationMatchesGoldenHash()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "adult_census_income.parquet");
        await using var readStream = System.IO.File.OpenRead(filePath);
        var allRecords = await AdultCensusRecordParquetExtensions.ReadParquetAsync(readStream);
        var slice = allRecords.Take(200).ToList();

        using var stream1 = new MemoryStream();
        await slice.WriteParquetAsync(
            stream1,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.Snappy,
            }
        );
        byte[] bytes1 = stream1.ToArray();

        using var stream2 = new MemoryStream();
        await slice.WriteParquetAsync(
            stream2,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.Snappy,
            }
        );
        byte[] bytes2 = stream2.ToArray();

        string hash1 = ComputeSha256(bytes1);
        string hash2 = ComputeSha256(bytes2);

        Assert.Equal(hash1, hash2);
        Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
        Assert.Equal("86dd82d1b8582da34878745ae5fb4314aba6105c9b1fdd6ca75598c5f4f0aff5", hash1);
    }

    [Fact]
    public async Task DiamondsSerializationMatchesGoldenHash()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "diamonds.parquet");
        await using var readStream = System.IO.File.OpenRead(filePath);
        var allRecords = await DiamondRecordParquetExtensions.ReadParquetAsync(readStream);
        var slice = allRecords.Take(200).ToList();

        using var stream1 = new MemoryStream();
        await slice.WriteParquetAsync(
            stream1,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.Snappy,
            }
        );
        byte[] bytes1 = stream1.ToArray();

        using var stream2 = new MemoryStream();
        await slice.WriteParquetAsync(
            stream2,
            options: new ParquetSerializerOptions
            {
                CompressionMethod = ParquetCompressionMethod.Snappy,
            }
        );
        byte[] bytes2 = stream2.ToArray();

        string hash1 = ComputeSha256(bytes1);
        string hash2 = ComputeSha256(bytes2);

        Assert.Equal(hash1, hash2);
        Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()));
        Assert.Equal("c88ea3dacd504ac83ba8c09a4d2757c490ba8de6665f519d34998f1de0b7facf", hash1);
    }

    // =========================================================================
    // 3. Checked-In Test Datasets Cryptographic Hash Integrity Tests
    //
    // Trait-gated as Category=DatasetIntegrity. CI runs these BEFORE the
    // regenerate-datasets steps (against the pristine LFS-hydrated checkout):
    // the regeneration steps overwrite test/data and test/data_csharp with
    // writer-version-specific bytes, so the pinned hashes only match a clean
    // checkout. The main post-regeneration run filters them out.
    // =========================================================================

    private static readonly Dictionary<string, string> ExpectedDatasetHashes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        // Format Specification v1.0 (PyArrow)
        ["test/data/v1/01_small_flat_primitives.parquet"] =
            "ba818b447502ce8b3c7f0d74d35066b53891f047c0398c5409010af2ffb56a51",
        ["test/data/v1/02_medium_nullable_types.parquet"] =
            "25ad07074cee27c0887e02ab9c4a9eb79fe49fcf831fca5a587ef59edb75c654",
        ["test/data/v1/03_complex_decimals_guids.parquet"] =
            "99a8d3ebd8dc80ebbe9583312fc1668a204c6157769171bcdde4d61afc50d386",
        ["test/data/v1/04_nested_lists_maps.parquet"] =
            "de7a27892cbb899d0ee22ced61bab65d03cb6a858e55fd9f3c0f6c1223ea43d7",
        ["test/data/v1/05_large_scale_flat.parquet"] =
            "c8e6f9f6e7674dcb43f21cd4051ec07b6f9a50eeda1c60538d81cbed58d64656",

        // Format Specification v2.6 (PyArrow)
        ["test/data/v2/01_small_flat_primitives.parquet"] =
            "e10903436c633f67b254273497dd1c33316a6cd1597acc2270a73555b649f817",
        ["test/data/v2/02_medium_nullable_types.parquet"] =
            "894522f43a79a8185ff8490ba85ad76ded06b91ba0032da57939142d3f8aff75",
        ["test/data/v2/03_complex_decimals_guids.parquet"] =
            "c4c19beb749f81245c58de1139ab682b6715e18369dbab7cba9acfa1ee244b13",
        ["test/data/v2/04_nested_lists_maps.parquet"] =
            "68d512da7e8c4aa3453915a3166662ed9c0d81870dff481a7f14cf7c35bf965b",
        ["test/data/v2/05_large_scale_flat.parquet"] =
            "cce252bdaae8189dd2f3109c7b93d84c6ab4f86a6c1b7c9119a2915fa21e8b5c",

        // C# Parquet.Net Datasets (v3)
        ["test/data_csharp/v3/01_small_flat_primitives.parquet"] =
            "dd431ffeb66018b4d85524cb6757508a26cd19f45c6d32fbd7502f944402aa4a",
        ["test/data_csharp/v3/02_medium_nullable_types.parquet"] =
            "9ee8f03169c95a51859626d97b457460e58f28b156582201950e5174704f7a14",
        ["test/data_csharp/v3/03_complex_decimals_guids.parquet"] =
            "ad41f9c9d6ac62da543591d037c56e799cfd1befbd0b61e752c18dd1650843c9",
        ["test/data_csharp/v3/05_large_scale_flat.parquet"] =
            "2f3639c2db21ea55ccf3c174fa70a9b4a0b44901416e186e9017490db27d3aee",

        // Git LFS Public Benchmark Datasets
        ["benchmarks/data/tpch_lineitem_sf001.parquet"] =
            "c2a3d37cff204e6569e35fa63f0efe0c89d43a36ac2dd25b8e65f90a1e9b0ccc",
        ["benchmarks/data/adult_census_income.parquet"] =
            "5a285f7b73234dda6fb69ea8bbd2655e850a3d9efd8c81512785afb1f7773517",
        ["benchmarks/data/diamonds.parquet"] =
            "828f91f368b79d520b200c393989e820adb7cbda7545fdf66b8552972467789e",
    };

    [Theory]
    [Trait("Category", "DatasetIntegrity")]
    [InlineData("test/data/v1/01_small_flat_primitives.parquet")]
    [InlineData("test/data/v1/02_medium_nullable_types.parquet")]
    [InlineData("test/data/v1/03_complex_decimals_guids.parquet")]
    [InlineData("test/data/v1/04_nested_lists_maps.parquet")]
    [InlineData("test/data/v1/05_large_scale_flat.parquet")]
    [InlineData("test/data/v2/01_small_flat_primitives.parquet")]
    [InlineData("test/data/v2/02_medium_nullable_types.parquet")]
    [InlineData("test/data/v2/03_complex_decimals_guids.parquet")]
    [InlineData("test/data/v2/04_nested_lists_maps.parquet")]
    [InlineData("test/data/v2/05_large_scale_flat.parquet")]
    [InlineData("test/data_csharp/v3/01_small_flat_primitives.parquet")]
    [InlineData("test/data_csharp/v3/02_medium_nullable_types.parquet")]
    [InlineData("test/data_csharp/v3/03_complex_decimals_guids.parquet")]
    [InlineData("test/data_csharp/v3/05_large_scale_flat.parquet")]
    [InlineData("benchmarks/data/tpch_lineitem_sf001.parquet")]
    [InlineData("benchmarks/data/adult_census_income.parquet")]
    [InlineData("benchmarks/data/diamonds.parquet")]
    public void CheckedInDatasetMatchesExpectedSha256(string relativePath)
    {
        string fullPath = Path.Combine(SolutionRoot, relativePath);
        Assert.True(System.IO.File.Exists(fullPath), $"Dataset file does not exist: {fullPath}");

        var fileInfo = new FileInfo(fullPath);
        Assert.True(
            fileInfo.Length > 1024,
            $"File is smaller than 1KB ({fileInfo.Length} bytes), likely unhydrated LFS pointer: {fullPath}"
        );

        using var fs = System.IO.File.OpenRead(fullPath);
        string actualHash = ComputeSha256(fs);
        string expectedHash = ExpectedDatasetHashes[relativePath];

        Assert.Equal(expectedHash, actualHash);
    }
}
