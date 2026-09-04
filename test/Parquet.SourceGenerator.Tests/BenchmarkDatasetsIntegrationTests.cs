using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record TpchLineItemRecord
{
    [ParquetColumn("l_orderkey")]
    public long? OrderKey { get; init; }

    [ParquetColumn("l_partkey")]
    public long? PartKey { get; init; }

    [ParquetColumn("l_suppkey")]
    public long? SuppKey { get; init; }

    [ParquetColumn("l_linenumber")]
    public long? LineNumber { get; init; }

    [ParquetColumn("l_quantity")]
    [ParquetDecimal(15, 2)]
    public decimal? Quantity { get; init; }

    [ParquetColumn("l_extendedprice")]
    [ParquetDecimal(15, 2)]
    public decimal? ExtendedPrice { get; init; }

    [ParquetColumn("l_discount")]
    [ParquetDecimal(15, 2)]
    public decimal? Discount { get; init; }

    [ParquetColumn("l_tax")]
    [ParquetDecimal(15, 2)]
    public decimal? Tax { get; init; }

    [ParquetColumn("l_returnflag")]
    public string? ReturnFlag { get; init; }

    [ParquetColumn("l_linestatus")]
    public string? LineStatus { get; init; }

    [ParquetColumn("l_shipdate")]
    public DateTime? ShipDate { get; init; }

    [ParquetColumn("l_commitdate")]
    public DateTime? CommitDate { get; init; }

    [ParquetColumn("l_receiptdate")]
    public DateTime? ReceiptDate { get; init; }

    [ParquetColumn("l_shipinstruct")]
    public string? ShipInstruct { get; init; }

    [ParquetColumn("l_shipmode")]
    public string? ShipMode { get; init; }

    [ParquetColumn("l_comment")]
    public string? Comment { get; init; }
}

[ParquetSerializable]
public partial record AdultCensusRecord
{
    [ParquetColumn("age")]
    public long? Age { get; init; }

    [ParquetColumn("workclass")]
    public string? Workclass { get; init; }

    [ParquetColumn("fnlwgt")]
    public long? Fnlwgt { get; init; }

    [ParquetColumn("education")]
    public string? Education { get; init; }

    [ParquetColumn("education.num")]
    public long? EducationNum { get; init; }

    [ParquetColumn("marital.status")]
    public string? MaritalStatus { get; init; }

    [ParquetColumn("occupation")]
    public string? Occupation { get; init; }

    [ParquetColumn("relationship")]
    public string? Relationship { get; init; }

    [ParquetColumn("race")]
    public string? Race { get; init; }

    [ParquetColumn("sex")]
    public string? Sex { get; init; }

    [ParquetColumn("capital.gain")]
    public long? CapitalGain { get; init; }

    [ParquetColumn("capital.loss")]
    public long? CapitalLoss { get; init; }

    [ParquetColumn("hours.per.week")]
    public long? HoursPerWeek { get; init; }

    [ParquetColumn("native.country")]
    public string? NativeCountry { get; init; }

    [ParquetColumn("income")]
    public string? Income { get; init; }
}

[ParquetSerializable]
public partial record DiamondRecord
{
    [ParquetColumn("carat")]
    public double? Carat { get; init; }

    [ParquetColumn("cut")]
    public long? Cut { get; init; }

    [ParquetColumn("color")]
    public long? Color { get; init; }

    [ParquetColumn("clarity")]
    public long? Clarity { get; init; }

    [ParquetColumn("depth")]
    public double? Depth { get; init; }

    [ParquetColumn("table")]
    public double? Table { get; init; }

    [ParquetColumn("x")]
    public double? X { get; init; }

    [ParquetColumn("y")]
    public double? Y { get; init; }

    [ParquetColumn("z")]
    public double? Z { get; init; }

    [ParquetColumn("price")]
    public double? Price { get; init; }
}

public sealed class BenchmarkDatasetsIntegrationTests
{
    private static readonly string BenchmarkDataRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "benchmarks", "data"));

    [Fact]
    public async Task ReadParquetAsyncDeserializesTpchLineitemDataset()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "tpch_lineitem_sf001.parquet");
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        await using var stream = System.IO.File.OpenRead(filePath);
        var records = await TpchLineItemRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(60175, records.Count);

        // Verify first record values from known TPC-H SF 0.01 ground truth
        var r0 = records[0];
        Assert.Equal(1L, r0.OrderKey);
        Assert.Equal(1552L, r0.PartKey);
        Assert.Equal(93L, r0.SuppKey);
        Assert.Equal(1L, r0.LineNumber);
        Assert.Equal(17.00m, r0.Quantity);
        Assert.Equal(24710.35m, r0.ExtendedPrice);
        Assert.Equal(0.04m, r0.Discount);
        Assert.Equal(0.02m, r0.Tax);
        Assert.Equal("N", r0.ReturnFlag);
        Assert.Equal("O", r0.LineStatus);
        Assert.Equal(new DateTime(1996, 3, 13), r0.ShipDate!.Value.Date);
        Assert.Equal(new DateTime(1996, 2, 12), r0.CommitDate!.Value.Date);
        Assert.Equal(new DateTime(1996, 3, 22), r0.ReceiptDate!.Value.Date);
        Assert.Equal("DELIVER IN PERSON", r0.ShipInstruct);
        Assert.Equal("TRUCK", r0.ShipMode);
        Assert.Equal("to beans x-ray carefull", r0.Comment);

        // Verify last record
        var rLast = records[60174];
        Assert.Equal(60000L, rLast.OrderKey);
    }

    [Fact]
    public async Task ReadParquetAsyncDeserializesAdultCensusIncomeDataset()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "adult_census_income.parquet");
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        await using var stream = System.IO.File.OpenRead(filePath);
        var records = await AdultCensusRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(32561, records.Count);

        var r0 = records[0];
        Assert.Equal(90L, r0.Age);
        Assert.Equal("?", r0.Workclass);
        Assert.Equal(77053L, r0.Fnlwgt);
        Assert.Equal("HS-grad", r0.Education);
        Assert.Equal(9L, r0.EducationNum);
        Assert.Equal("Widowed", r0.MaritalStatus);
        Assert.Equal("?", r0.Occupation);
        Assert.Equal("Not-in-family", r0.Relationship);
        Assert.Equal("White", r0.Race);
        Assert.Equal("Female", r0.Sex);
        Assert.Equal(0L, r0.CapitalGain);
        Assert.Equal(4356L, r0.CapitalLoss);
        Assert.Equal(40L, r0.HoursPerWeek);
        Assert.Equal("United-States", r0.NativeCountry);
        Assert.Equal("<=50K", r0.Income);
    }

    [Fact]
    public async Task ReadParquetAsyncDeserializesDiamondsDataset()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "diamonds.parquet");
        Assert.True(System.IO.File.Exists(filePath), $"File not found: {filePath}");

        await using var stream = System.IO.File.OpenRead(filePath);
        var records = await DiamondRecordParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(53940, records.Count);

        var r0 = records[0];
        Assert.Equal(0.23, r0.Carat!.Value, precision: 2);
        Assert.Equal(2L, r0.Cut);
        Assert.Equal(1L, r0.Color);
        Assert.Equal(3L, r0.Clarity);
        Assert.Equal(61.5, r0.Depth!.Value, precision: 1);
        Assert.Equal(55.0, r0.Table!.Value, precision: 1);
        Assert.Equal(3.95, r0.X!.Value, precision: 2);
        Assert.Equal(3.98, r0.Y!.Value, precision: 2);
        Assert.Equal(2.43, r0.Z!.Value, precision: 2);
    }

    [Fact]
    public async Task TpchLineItemRoundtripsWithSnappyAndZstd()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "tpch_lineitem_sf001.parquet");
        await using var stream = System.IO.File.OpenRead(filePath);
        var original = await TpchLineItemRecordParquetExtensions.ReadParquetAsync(stream);

        // Round-trip with Snappy
        using var snappyStream = new MemoryStream();
        var snappyOptions = new ParquetSerializerOptions
        {
            CompressionMethod = ParquetCompressionMethod.Snappy,
            RowGroupSize = 20_000
        };
        await original.WriteParquetAsync(snappyStream, options: snappyOptions);
        snappyStream.Position = 0;

        var roundtrippedSnappy = await TpchLineItemRecordParquetExtensions.ReadParquetAsync(snappyStream);
        Assert.Equal(original.Count, roundtrippedSnappy.Count);
        Assert.Equal(original[0].OrderKey, roundtrippedSnappy[0].OrderKey);
        Assert.Equal(original[0].Quantity, roundtrippedSnappy[0].Quantity);
        Assert.Equal(original[0].Comment, roundtrippedSnappy[0].Comment);

        // Round-trip with Zstd
        using var zstdStream = new MemoryStream();
        var zstdOptions = new ParquetSerializerOptions
        {
            CompressionMethod = ParquetCompressionMethod.Zstd,
            CompressionLevel = ParquetCompressionLevel.Optimal,
            RowGroupSize = 20_000
        };
        await original.WriteParquetAsync(zstdStream, options: zstdOptions);
        zstdStream.Position = 0;

        var roundtrippedZstd = await TpchLineItemRecordParquetExtensions.ReadParquetAsync(zstdStream);
        Assert.Equal(original.Count, roundtrippedZstd.Count);
        Assert.Equal(original[100].ExtendedPrice, roundtrippedZstd[100].ExtendedPrice);
        Assert.Equal(original[100].ShipInstruct, roundtrippedZstd[100].ShipInstruct);
    }

    [Fact]
    public async Task AdultCensusRoundtripsWithParallelReader()
    {
        string filePath = Path.Combine(BenchmarkDataRoot, "adult_census_income.parquet");
        await using var stream = System.IO.File.OpenRead(filePath);
        var original = await AdultCensusRecordParquetExtensions.ReadParquetAsync(stream);

        using var outputStream = new MemoryStream();
        await original.WriteParquetBatchedAsync(outputStream, rowGroupSize: 5_000);
        byte[] bytes = outputStream.ToArray();

        var roundtripped = await AdultCensusRecordParquetExtensions.ReadParquetParallelAsync(
            new ReadOnlyMemory<byte>(bytes),
            maxDegreeOfParallelism: 4
        );

        Assert.Equal(original.Count, roundtripped.Count);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(original[i].Workclass, roundtripped[i].Workclass);
            Assert.Equal(original[i].Education, roundtripped[i].Education);
            Assert.Equal(original[i].Income, roundtripped[i].Income);
        }
    }
}
