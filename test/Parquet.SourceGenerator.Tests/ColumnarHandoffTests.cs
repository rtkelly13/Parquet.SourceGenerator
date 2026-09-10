using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Models;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Flat model exercising every buffer shape the columnar hand-off has to express: fixed-width
/// values, a nullable value column (packed payload + definition levels), a string column and a
/// binary column.
/// </summary>
[ParquetSerializable]
public partial record ColumnarHandoffModel
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string? Name { get; init; }

    [ParquetColumn("score")]
    public double Score { get; init; }

    [ParquetColumn("optional_score")]
    public double? OptionalScore { get; init; }

    [ParquetColumn("flag")]
    public bool Flag { get; init; }

    [ParquetColumn("correlation_id")]
    public Guid CorrelationId { get; init; }

    [ParquetColumn("created_at")]
    public DateTime CreatedAt { get; init; }

    [ParquetColumn("optional_count")]
    public long? OptionalCount { get; init; }

    [ParquetColumn("payload")]
    public byte[]? Payload { get; init; }
}

/// <summary>
/// Model with no nullable value columns — checks the generated surface degrades to the simple
/// shape (no definition-level parameters at all).
/// </summary>
[ParquetSerializable]
public partial record ColumnarDenseModel
{
    [ParquetColumn("a")]
    public long A { get; init; }

    [ParquetColumn("b")]
    public double B { get; init; }
}

/// <summary>
/// Direct columnar hand-off (issue #137). The generated <c>WriteParquetRowGroupAsync(batch)</c> /
/// <c>WriteParquetRowGroupColumnarAsync(...)</c> pair must produce output indistinguishable from
/// the row-oriented path for the same logical data, while renting nothing.
/// </summary>
public sealed class ColumnarHandoffTests
{
    private const int RowCount = 512;

    private static List<ColumnarHandoffModel> BuildRows(int count)
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<ColumnarHandoffModel>(count);
        for (int i = 0; i < count; i++)
        {
            rows.Add(
                new ColumnarHandoffModel
                {
                    Id = i,
                    // Every third row nulls the string, so the null lane is exercised too.
                    Name = i % 3 == 0 ? null : "row-" + i.ToString(CultureInfo.InvariantCulture),
                    Score = i * 1.5,
                    OptionalScore = i % 4 == 0 ? null : i * 0.25,
                    Flag = i % 2 == 0,
                    CorrelationId = new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                    CreatedAt = baseTime.AddMilliseconds(i),
                    OptionalCount = i % 5 == 0 ? null : i * 1000L,
                    Payload = i % 7 == 0 ? null : new byte[] { (byte)i, (byte)(i + 1) },
                }
            );
        }

        return rows;
    }

    /// <summary>
    /// Transposes a POCO list into the exact buffer shapes the generated batch struct expects.
    /// A real caller would already hold these; the test builds them so the two write paths can be
    /// compared on identical data.
    /// </summary>
    private static ColumnarHandoffModelColumnarBatch Transpose(List<ColumnarHandoffModel> rows)
    {
        int count = rows.Count;
        var id = new int[count];
        var name = new ReadOnlyMemory<char>?[count];
        var score = new double[count];
        var optionalScore = new double[count];
        var optionalScoreDefs = new int[count];
        var flag = new bool[count];
        var correlationId = new Guid[count];
        var createdAt = new DateTime[count];
        var optionalCount = new long[count];
        var optionalCountDefs = new int[count];
        var payload = new ReadOnlyMemory<byte>?[count];

        int scorePacked = 0;
        int countPacked = 0;
        for (int i = 0; i < count; i++)
        {
            ColumnarHandoffModel row = rows[i];
            id[i] = row.Id;
            // The generated converter, not a bare conditional: `cond ? null : x.AsMemory()` has
            // natural type ReadOnlyMemory<char>, so the null branch would store a
            // present-but-empty value instead of a null.
            name[i] = ColumnarHandoffModelParquetExtensions.AsColumnarText(row.Name);
            score[i] = row.Score;
            flag[i] = row.Flag;
            correlationId[i] = row.CorrelationId;
            createdAt[i] = row.CreatedAt;
            payload[i] = ColumnarHandoffModelParquetExtensions.AsColumnarBinary(row.Payload);

            if (row.OptionalScore.HasValue)
            {
                optionalScore[scorePacked++] = row.OptionalScore.Value;
                optionalScoreDefs[i] = 1;
            }

            if (row.OptionalCount.HasValue)
            {
                optionalCount[countPacked++] = row.OptionalCount.Value;
                optionalCountDefs[i] = 1;
            }
        }

        return new ColumnarHandoffModelColumnarBatch
        {
            RowCount = count,
            Id = id,
            Name = name,
            Score = score,
            OptionalScore = optionalScore.AsMemory(0, scorePacked),
            OptionalScoreDefinitionLevels = optionalScoreDefs,
            Flag = flag,
            CorrelationId = correlationId,
            CreatedAt = createdAt,
            OptionalCount = optionalCount.AsMemory(0, countPacked),
            OptionalCountDefinitionLevels = optionalCountDefs,
            Payload = payload,
        };
    }

    [Fact]
    public async Task ColumnarWriteRoundTripsToTheSameValuesAsTheRowOrientedWrite()
    {
        List<ColumnarHandoffModel> rows = BuildRows(RowCount);

        using var rowStream = new MemoryStream();
        await rows.WriteParquetAsync(rowStream);

        using var columnarStream = new MemoryStream();
        await Transpose(rows).WriteParquetAsync(columnarStream);

        rowStream.Position = 0;
        columnarStream.Position = 0;
        List<ColumnarHandoffModel> fromRows =
            await ColumnarHandoffModelParquetExtensions.ReadParquetAsync(rowStream);
        List<ColumnarHandoffModel> fromColumns =
            await ColumnarHandoffModelParquetExtensions.ReadParquetAsync(columnarStream);

        Assert.Equal(RowCount, fromColumns.Count);
        Assert.Equal(fromRows.Count, fromColumns.Count);
        for (int i = 0; i < fromRows.Count; i++)
        {
            Assert.Equal(fromRows[i].Id, fromColumns[i].Id);
            Assert.Equal(fromRows[i].Name, fromColumns[i].Name);
            Assert.Equal(fromRows[i].Score, fromColumns[i].Score);
            Assert.Equal(fromRows[i].OptionalScore, fromColumns[i].OptionalScore);
            Assert.Equal(fromRows[i].Flag, fromColumns[i].Flag);
            Assert.Equal(fromRows[i].CorrelationId, fromColumns[i].CorrelationId);
            Assert.Equal(fromRows[i].CreatedAt, fromColumns[i].CreatedAt);
            Assert.Equal(fromRows[i].OptionalCount, fromColumns[i].OptionalCount);
            Assert.Equal(fromRows[i].Payload, fromColumns[i].Payload);
        }
    }

    [Fact]
    public async Task ColumnarWriteProducesByteIdenticalOutputToTheRowOrientedWrite()
    {
        List<ColumnarHandoffModel> rows = BuildRows(RowCount);

        using var rowStream = new MemoryStream();
        await rows.WriteParquetAsync(rowStream);

        using var columnarStream = new MemoryStream();
        await Transpose(rows).WriteParquetAsync(columnarStream);

        // The columnar path reaches the same encoder with the same buffers, so the file bytes are
        // the same too. This is a stronger statement than value equality: it says the hand-off is
        // not merely equivalent but literally the same write.
        Assert.Equal(rowStream.ToArray(), columnarStream.ToArray());
    }

    [Fact]
    public async Task PositionalOverloadMatchesTheBatchOverload()
    {
        List<ColumnarHandoffModel> rows = BuildRows(RowCount);
        ColumnarHandoffModelColumnarBatch batch = Transpose(rows);

        using var batchStream = new MemoryStream();
        await batch.WriteParquetAsync(batchStream);

        using var positionalStream = new MemoryStream();
        await using (
            var writer = await ParquetWriter.CreateAsync(
                ColumnarHandoffModelParquetExtensions.Schema,
                positionalStream
            )
        )
        {
            await writer.WriteParquetRowGroupColumnarAsync(
                batch.RowCount,
                batch.Id,
                batch.Name,
                batch.Score,
                batch.OptionalScore,
                batch.OptionalScoreDefinitionLevels,
                batch.Flag,
                batch.CorrelationId,
                batch.CreatedAt,
                batch.OptionalCount,
                batch.OptionalCountDefinitionLevels,
                batch.Payload
            );
        }

        Assert.Equal(batchStream.ToArray(), positionalStream.ToArray());
    }

    [Fact]
    public async Task OversizedBuffersAreSlicedToTheRowCount()
    {
        List<ColumnarHandoffModel> rows = BuildRows(RowCount);
        ColumnarHandoffModelColumnarBatch full = Transpose(rows);

        // A caller reusing a large scratch buffer supplies more entries than rows; only RowCount
        // of them may reach the file.
        const int shortCount = 100;
        ColumnarHandoffModelColumnarBatch sliced = full;
        sliced.RowCount = shortCount;
        int packedScores = 0;
        int packedCounts = 0;
        for (int i = 0; i < shortCount; i++)
        {
            packedScores += full.OptionalScoreDefinitionLevels.Span[i];
            packedCounts += full.OptionalCountDefinitionLevels.Span[i];
        }
        sliced.OptionalScore = full.OptionalScore.Slice(0, packedScores);
        sliced.OptionalCount = full.OptionalCount.Slice(0, packedCounts);

        using var stream = new MemoryStream();
        await sliced.WriteParquetAsync(stream);
        stream.Position = 0;
        List<ColumnarHandoffModel> read =
            await ColumnarHandoffModelParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(shortCount, read.Count);
        for (int i = 0; i < shortCount; i++)
        {
            Assert.Equal(rows[i].Id, read[i].Id);
            Assert.Equal(rows[i].OptionalScore, read[i].OptionalScore);
            Assert.Equal(rows[i].OptionalCount, read[i].OptionalCount);
        }
    }

    [Fact]
    public async Task EmptyBatchWritesNoRowGroup()
    {
        var batch = new ColumnarHandoffModelColumnarBatch { RowCount = 0 };

        using var stream = new MemoryStream();
        await batch.WriteParquetAsync(stream);
        stream.Position = 0;
        List<ColumnarHandoffModel> read =
            await ColumnarHandoffModelParquetExtensions.ReadParquetAsync(stream);

        Assert.Empty(read);
    }

    [Fact]
    public async Task ShortValueColumnIsRejectedWithTheColumnName()
    {
        List<ColumnarHandoffModel> rows = BuildRows(RowCount);
        ColumnarHandoffModelColumnarBatch batch = Transpose(rows);
        batch.Score = batch.Score.Slice(0, RowCount - 1);

        using var stream = new MemoryStream();
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await batch.WriteParquetAsync(stream)
        );
        Assert.Contains("Score", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortDefinitionLevelColumnIsRejectedWithTheColumnName()
    {
        List<ColumnarHandoffModel> rows = BuildRows(RowCount);
        ColumnarHandoffModelColumnarBatch batch = Transpose(rows);
        batch.OptionalScoreDefinitionLevels = batch.OptionalScoreDefinitionLevels.Slice(
            0,
            RowCount - 1
        );

        using var stream = new MemoryStream();
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await batch.WriteParquetAsync(stream)
        );
        Assert.Contains("OptionalScore", error.Message, StringComparison.Ordinal);
        Assert.Contains("definition levels", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NegativeRowCountIsRejected()
    {
        var batch = new ColumnarHandoffModelColumnarBatch { RowCount = -1 };
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await batch.WriteParquetAsync(stream)
        );
    }

    private static readonly long[] DenseA = [1, 2, 3];
    private static readonly double[] DenseB = [1.5, 2.5, 3.5];

    [Fact]
    public async Task DenseModelWithoutNullableColumnsExposesNoDefinitionLevelParameters()
    {
        var batch = new ColumnarDenseModelColumnarBatch
        {
            RowCount = 3,
            A = DenseA,
            B = DenseB,
        };

        using var stream = new MemoryStream();
        await batch.WriteParquetAsync(stream);
        stream.Position = 0;
        List<ColumnarDenseModel> read = await ColumnarDenseModelParquetExtensions.ReadParquetAsync(
            stream
        );

        Assert.Equal(DenseA, read.Select(r => r.A).ToArray());
        Assert.Equal(DenseB, read.Select(r => r.B).ToArray());
    }

    [Fact]
    public async Task MultipleBatchesAppendAsSeparateRowGroups()
    {
        List<ColumnarHandoffModel> rows = BuildRows(RowCount);
        ColumnarHandoffModelColumnarBatch batch = Transpose(rows);

        using var stream = new MemoryStream();
        await using (
            var writer = await ParquetWriter.CreateAsync(
                ColumnarHandoffModelParquetExtensions.Schema,
                stream
            )
        )
        {
            await writer.WriteParquetRowGroupAsync(batch);
            await writer.WriteParquetRowGroupAsync(batch);
        }

        stream.Position = 0;
        List<ColumnarHandoffModel> read =
            await ColumnarHandoffModelParquetExtensions.ReadParquetAsync(stream);
        Assert.Equal(RowCount * 2, read.Count);
        Assert.Equal(rows[7].Name, read[RowCount + 7].Name);
    }

    // ──────────────────────────────────────────────────────────
    //  Emitted-source guarantees
    // ──────────────────────────────────────────────────────────

    private static string EmitFlatSource() =>
        CodeEmitter.EmitSource(
            new TargetClassModel(
                Namespace: "TestNamespace",
                ClassName: "Widget",
                Properties: new EquatableArray<PropertyModel>(
                    new PropertyModel[]
                    {
                        new(
                            "Key",
                            "key",
                            "long",
                            null,
                            null,
                            1,
                            null,
                            null,
                            PropertyKind.Primitive,
                            false
                        ),
                        new(
                            "Label",
                            "label",
                            "string",
                            null,
                            null,
                            2,
                            null,
                            null,
                            PropertyKind.Primitive,
                            true
                        ),
                        new(
                            "Weight",
                            "weight",
                            "double?",
                            null,
                            null,
                            3,
                            null,
                            null,
                            PropertyKind.Primitive,
                            true
                        ),
                    }
                )
            )
        );

    [Fact]
    public void ColumnarPathRentsNothingFromArrayPool()
    {
        string source = EmitFlatSource();
        string columnar = ExtractColumnarRegion(source);

        // The whole point of the hand-off: caller-owned buffers reach Parquet.Net untouched.
        Assert.DoesNotContain("ArrayPool", columnar, StringComparison.Ordinal);
        Assert.DoesNotContain(".Rent(", columnar, StringComparison.Ordinal);
        // ...and nothing is copied into a scratch array either.
        Assert.DoesNotContain(".CopyTo(", columnar, StringComparison.Ordinal);
        Assert.DoesNotContain("new long[", columnar, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableValueColumnGetsSeparateValuesAndDefinitionLevelParameters()
    {
        string source = EmitFlatSource();

        Assert.Contains(
            "public global::System.ReadOnlyMemory<double> Weight;",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "public global::System.ReadOnlyMemory<int> WeightDefinitionLevels;",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "global::System.ReadOnlyMemory<double> weight,",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "global::System.ReadOnlyMemory<int> weightDefinitionLevels,",
            source,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void CompoundModelsDoNotGetAColumnarSurface()
    {
        var nested = new PropertyModel(
            "City",
            "city",
            "string",
            null,
            null,
            1,
            null,
            null,
            PropertyKind.Primitive,
            true
        );
        var structProp = new PropertyModel(
            "Ship",
            "ship",
            "global::App.Address",
            null,
            null,
            1,
            null,
            null,
            PropertyKind.Struct,
            false
        )
        {
            Children = new EquatableArray<PropertyModel>(new[] { nested }),
        };

        string source = CodeEmitter.EmitSource(
            new TargetClassModel(
                Namespace: "TestNamespace",
                ClassName: "Shipment",
                Properties: new EquatableArray<PropertyModel>(new[] { structProp })
            )
        );

        // Struct/list members carry a definition ladder a caller cannot express as flat buffers,
        // so those models keep the row-oriented API only (documented limitation of #137).
        Assert.DoesNotContain("ColumnarBatch", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "WriteParquetRowGroupColumnarAsync",
            source,
            StringComparison.Ordinal
        );
    }

    private static string ExtractColumnarRegion(string source)
    {
        int start = source.IndexOf(
            "Writes one row group directly from caller-owned column buffers",
            StringComparison.Ordinal
        );
        Assert.True(start >= 0, "columnar writer was not emitted");
        int end = source.IndexOf("Asynchronously serializes all", start, StringComparison.Ordinal);
        Assert.True(end > start, "could not find the end of the columnar region");
        return source.Substring(start, end - start);
    }
}
