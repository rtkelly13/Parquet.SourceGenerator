using System;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

public sealed class AttributesTests
{
    [Fact]
    public void ParquetSerializerOptionsCarriesCorrectDefaults()
    {
        var options = ParquetSerializerOptions.Default;

        Assert.NotNull(options);
        Assert.Equal(50_000, options.RowGroupSize);
        Assert.Equal(-1, options.MaxDegreeOfParallelism);
        Assert.Equal(ParquetCompressionMethod.Snappy, options.CompressionMethod);
        Assert.Null(options.CompressionLevel);
        Assert.False(options.DeduplicateStrings);
        Assert.Null(options.DictionaryEncodingThreshold);
        Assert.Null(options.DictionaryEncodingSampleSize);
        Assert.NotNull(options.ColumnEncodingHints);
        Assert.Empty(options.ColumnEncodingHints);

        // Default returns a fresh instance each time to prevent accidental mutation of shared state
        var options2 = ParquetSerializerOptions.Default;
        Assert.NotSame(options, options2);
    }

    [Fact]
    public void ParquetSerializerOptionsCanBeMutated()
    {
        var options = new ParquetSerializerOptions
        {
            RowGroupSize = 10_000,
            MaxDegreeOfParallelism = 4,
            CompressionMethod = ParquetCompressionMethod.Zstd,
            CompressionLevel = ParquetCompressionLevel.SmallestSize,
            DeduplicateStrings = true,
            DictionaryEncodingThreshold = 0.5,
            DictionaryEncodingSampleSize = 500,
            ColumnEncodingHints = { ["test_col"] = ParquetColumnEncoding.DeltaBinaryPacked },
        };

        Assert.Equal(10_000, options.RowGroupSize);
        Assert.Equal(4, options.MaxDegreeOfParallelism);
        Assert.Equal(ParquetCompressionMethod.Zstd, options.CompressionMethod);
        Assert.Equal(ParquetCompressionLevel.SmallestSize, options.CompressionLevel);
        Assert.True(options.DeduplicateStrings);
        Assert.Equal(0.5, options.DictionaryEncodingThreshold);
        Assert.Equal(500, options.DictionaryEncodingSampleSize);
        Assert.Single(options.ColumnEncodingHints);
        Assert.Equal(
            ParquetColumnEncoding.DeltaBinaryPacked,
            options.ColumnEncodingHints["test_col"]
        );
    }

    [Theory]
    [InlineData(ParquetColumnEncoding.Default)]
    [InlineData(ParquetColumnEncoding.Dictionary)]
    [InlineData(ParquetColumnEncoding.DeltaBinaryPacked)]
    [InlineData(ParquetColumnEncoding.ByteSplitStream)]
    public void ParquetColumnEncodingEnumValuesAreValid(ParquetColumnEncoding encoding)
    {
        Assert.True(Enum.IsDefined(encoding));
    }

    [Theory]
    [InlineData(ParquetCompressionMethod.None)]
    [InlineData(ParquetCompressionMethod.Snappy)]
    [InlineData(ParquetCompressionMethod.Gzip)]
    [InlineData(ParquetCompressionMethod.Lz4)]
    [InlineData(ParquetCompressionMethod.Brotli)]
    [InlineData(ParquetCompressionMethod.Zstd)]
    public void ParquetCompressionMethodEnumValuesAreValid(ParquetCompressionMethod method)
    {
        Assert.True(Enum.IsDefined(method));
    }

    [Theory]
    [InlineData(ParquetCompressionLevel.Optimal)]
    [InlineData(ParquetCompressionLevel.Fastest)]
    [InlineData(ParquetCompressionLevel.NoCompression)]
    [InlineData(ParquetCompressionLevel.SmallestSize)]
    public void ParquetCompressionLevelEnumValuesAreValid(ParquetCompressionLevel level)
    {
        Assert.True(Enum.IsDefined(level));
    }

    [Fact]
    public void ParquetColumnAttributeStoresProperties()
    {
        var attr = new ParquetColumnAttribute("test_col")
        {
            Order = 42,
            Encoding = ParquetColumnEncoding.DeltaBinaryPacked,
        };

        Assert.Equal("test_col", attr.Name);
        Assert.Equal(42, attr.Order);
        Assert.Equal(ParquetColumnEncoding.DeltaBinaryPacked, attr.Encoding);
    }

    [Fact]
    public void ParquetDecimalAttributeStoresProperties()
    {
        var attr = new ParquetDecimalAttribute(18, 4);

        Assert.Equal(18, attr.Precision);
        Assert.Equal(4, attr.Scale);
    }

    [Fact]
    public void ParquetTimestampAttributeStoresProperties()
    {
        var attrMs = new ParquetTimestampAttribute(ParquetTimestampUnit.Milliseconds);
        Assert.Equal(ParquetTimestampUnit.Milliseconds, attrMs.Unit);

        var attrUs = new ParquetTimestampAttribute(ParquetTimestampUnit.Microseconds);
        Assert.Equal(ParquetTimestampUnit.Microseconds, attrUs.Unit);
    }

    [Fact]
    public void ParquetIgnoreAttributeCanBeInstantiated()
    {
        var attr = new ParquetIgnoreAttribute();
        Assert.NotNull(attr);
    }

    [Fact]
    public void ParquetSerializableAttributeCanBeInstantiated()
    {
        var defaultAttr = new ParquetSerializableAttribute();
        Assert.NotNull(defaultAttr);
    }
}
