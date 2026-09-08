using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Every leaf kind the Arrow bridge claims to map, in one row type, so a round trip covers the
/// mapping table rather than a sample of it.
/// </summary>
[ParquetSerializable]
public partial record ArrowExportRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("big")]
    public long Big { get; init; }

    [ParquetColumn("small")]
    public short Small { get; init; }

    [ParquetColumn("tiny")]
    public byte Tiny { get; init; }

    [ParquetColumn("ratio")]
    public double Ratio { get; init; }

    [ParquetColumn("weight")]
    public float Weight { get; init; }

    [ParquetColumn("flag")]
    public bool Flag { get; init; }

    [ParquetColumn("name")]
    public string? Name { get; init; }

    [ParquetColumn("price")]
    [ParquetDecimal(18, 4)]
    public decimal Price { get; init; }

    [ParquetColumn("created_at")]
    public DateTime CreatedAt { get; init; }

    [ParquetColumn("elapsed")]
    public TimeSpan Elapsed { get; init; }

    [ParquetColumn("clock")]
    public TimeOnly Clock { get; init; }

    [ParquetColumn("day")]
    public DateOnly Day { get; init; }

    [ParquetColumn("correlation_id")]
    public Guid CorrelationId { get; init; }

    [ParquetColumn("payload")]
    public byte[]? Payload { get; init; }

    [ParquetColumn("status")]
    public ArrowExportStatus Status { get; init; }
}

/// <summary>Enum leaf, exported through its underlying integer.</summary>
public enum ArrowExportStatus
{
    /// <summary>Zero.</summary>
    Pending = 0,

    /// <summary>One.</summary>
    Shipped = 1,

    /// <summary>Two.</summary>
    Cancelled = 2,
}

/// <summary>
/// Nullable counterparts, exercising the validity-bitmap half of the bridge including the
/// empty-string-versus-null case that a naive length check collapses.
/// </summary>
[ParquetSerializable]
public partial record ArrowNullableExportRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("maybe_int")]
    public int? MaybeInt { get; init; }

    [ParquetColumn("maybe_flag")]
    public bool? MaybeFlag { get; init; }

    [ParquetColumn("maybe_name")]
    public string? MaybeName { get; init; }

    [ParquetColumn("maybe_price")]
    [ParquetDecimal(12, 3)]
    public decimal? MaybePrice { get; init; }

    [ParquetColumn("maybe_when")]
    public DateTime? MaybeWhen { get; init; }

    [ParquetColumn("maybe_guid")]
    public Guid? MaybeGuid { get; init; }

    [ParquetColumn("maybe_payload")]
    public byte[]? MaybePayload { get; init; }
}

/// <summary>
/// Parquet's INTERVAL primitive, which Arrow expresses as a month/day/nanosecond interval.
/// </summary>
[ParquetSerializable]
public partial record ArrowIntervalRecord
{
    /// <summary>Row identity.</summary>
    [ParquetColumn("id")]
    public int Id { get; init; }

    /// <summary>The interval column under test.</summary>
    [ParquetColumn("span")]
    public global::Parquet.File.Values.Primitives.Interval Span { get; init; }
}

/// <summary>
/// Read-side Arrow bridge (#178): conditional emission gate, mapping fidelity per primitive kind,
/// validity bitmaps, batch splitting, and the no-POCO guarantee.
/// </summary>
public sealed class ArrowRecordBatchExportTests
{
    private static readonly int[] SplitBatchLengths = [2, 2, 1];

    private static readonly DateTime BaseInstant = new(2024, 3, 17, 11, 22, 33, DateTimeKind.Utc);

    private static List<ArrowExportRecord> SampleRows(int count) =>
        Enumerable
            .Range(0, count)
            .Select(i => new ArrowExportRecord
            {
                Id = i,
                Big = 5_000_000_000L + i,
                Small = (short)(i - 3),
                Tiny = (byte)(i + 7),
                Ratio = i * 1.5,
                Weight = i * 0.25f,
                Flag = i % 2 == 0,
                Name = i == 1 ? string.Empty : $"row-{i}",
                Price = 12.3456m + i,
                CreatedAt = BaseInstant.AddMinutes(i),
                Elapsed = TimeSpan.FromMilliseconds(1500 + i),
                Clock = new TimeOnly(9, 30, 15).Add(TimeSpan.FromSeconds(i)),
                Day = new DateOnly(2024, 1, 5).AddDays(i),
                CorrelationId = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                Payload = i == 2 ? null : new byte[] { (byte)i, 0xAB, 0xCD },
                Status = (ArrowExportStatus)(i % 3),
            })
            .ToList();

    private static async Task<MemoryStream> WriteAsync(
        IReadOnlyList<ArrowExportRecord> rows,
        int rowGroupSize
    )
    {
        var stream = new MemoryStream();
        await rows.ToList().WriteParquetBatchedAsync(stream, rowGroupSize: rowGroupSize);
        stream.Position = 0;
        return stream;
    }

    private static async Task<List<RecordBatch>> CollectAsync(
        Stream stream,
        int? maxRowsPerBatch = null
    )
    {
        var batches = new List<RecordBatch>();
        await foreach (
            RecordBatch batch in ArrowExportRecordParquetExtensions.ReadParquetRecordBatchesAsync(
                stream,
                maxRowsPerBatch: maxRowsPerBatch
            )
        )
        {
            batches.Add(batch);
        }

        return batches;
    }

    [Fact]
    public async Task ExportsOneBatchPerRowGroup()
    {
        List<ArrowExportRecord> rows = SampleRows(5);
        using MemoryStream stream = await WriteAsync(rows, rowGroupSize: 2);

        List<RecordBatch> batches = await CollectAsync(stream);

        Assert.Equal(3, batches.Count);
        Assert.Equal(SplitBatchLengths, batches.Select(b => b.Length).ToArray());
        Assert.Equal(5, batches.Sum(b => b.Length));
        foreach (RecordBatch batch in batches)
        {
            batch.Dispose();
        }
    }

    [Fact]
    public async Task MaxRowsPerBatchSplitsRowGroupsFurther()
    {
        List<ArrowExportRecord> rows = SampleRows(5);
        using MemoryStream stream = await WriteAsync(rows, rowGroupSize: 5);

        List<RecordBatch> batches = await CollectAsync(stream, maxRowsPerBatch: 2);

        Assert.Equal(SplitBatchLengths, batches.Select(b => b.Length).ToArray());

        // Splitting must not reorder or drop rows: the id column reassembles the original sequence.
        var ids = new List<int>();
        foreach (RecordBatch batch in batches)
        {
            var idColumn = (Int32Array)batch.Column("id");
            for (int i = 0; i < idColumn.Length; i++)
            {
                ids.Add(idColumn.GetValue(i)!.Value);
            }

            batch.Dispose();
        }

        Assert.Equal(Enumerable.Range(0, 5).ToArray(), ids.ToArray());
    }

    [Fact]
    public async Task RejectsNonPositiveMaxRowsPerBatch()
    {
        List<ArrowExportRecord> rows = SampleRows(1);
        using MemoryStream stream = await WriteAsync(rows, rowGroupSize: 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (
                RecordBatch _ in ArrowExportRecordParquetExtensions.ReadParquetRecordBatchesAsync(
                    stream,
                    maxRowsPerBatch: 0
                )
            )
            {
                // The enumerator throws before yielding anything.
            }
        });
    }

    [Fact]
    public async Task RoundTripsEveryPrimitiveKind()
    {
        List<ArrowExportRecord> rows = SampleRows(4);
        using MemoryStream stream = await WriteAsync(rows, rowGroupSize: 4);

        List<RecordBatch> batches = await CollectAsync(stream);
        using RecordBatch batch = Assert.Single(batches);

        Assert.Equal(4, batch.Length);
        Assert.Equal(16, batch.ColumnCount);

        var ids = (Int32Array)batch.Column("id");
        var bigs = (Int64Array)batch.Column("big");
        var smalls = (Int16Array)batch.Column("small");
        var tinies = (UInt8Array)batch.Column("tiny");
        var ratios = (DoubleArray)batch.Column("ratio");
        var weights = (FloatArray)batch.Column("weight");
        var flags = (BooleanArray)batch.Column("flag");
        var names = (StringArray)batch.Column("name");
        var prices = (Decimal128Array)batch.Column("price");
        var created = (TimestampArray)batch.Column("created_at");
        var elapsed = (DurationArray)batch.Column("elapsed");
        var clocks = (Time64Array)batch.Column("clock");
        var days = (Date32Array)batch.Column("day");
        var guids = (Apache.Arrow.Arrays.FixedSizeBinaryArray)batch.Column("correlation_id");
        var payloads = (BinaryArray)batch.Column("payload");
        var statuses = (Int32Array)batch.Column("status");

        for (int i = 0; i < rows.Count; i++)
        {
            ArrowExportRecord expected = rows[i];
            Assert.Equal(expected.Id, ids.GetValue(i));
            Assert.Equal(expected.Big, bigs.GetValue(i));
            Assert.Equal(expected.Small, smalls.GetValue(i));
            Assert.Equal(expected.Tiny, tinies.GetValue(i));
            Assert.Equal(expected.Ratio, ratios.GetValue(i)!.Value, 6);
            Assert.Equal(expected.Weight, weights.GetValue(i)!.Value, 4);
            Assert.Equal(expected.Flag, flags.GetValue(i));
            Assert.Equal(expected.Name, names.GetString(i));
            Assert.Equal(expected.Price, prices.GetValue(i));
            Assert.Equal(
                new DateTimeOffset(expected.CreatedAt),
                created.GetTimestamp(i)!.Value.ToUniversalTime()
            );
            Assert.Equal(expected.Elapsed, elapsed.GetTimeSpan(i));
            Assert.Equal(expected.Clock.ToTimeSpan(), clocks.GetTime(i)!.Value.ToTimeSpan());
            Assert.Equal(expected.Day, days.GetDateOnly(i));
            Assert.Equal(UuidBytes(expected.CorrelationId), guids.GetBytes(i).ToArray());
            Assert.Equal((int)expected.Status, statuses.GetValue(i));

            if (expected.Payload is null)
            {
                Assert.True(payloads.IsNull(i));
            }
            else
            {
                Assert.Equal(expected.Payload, payloads.GetBytes(i).ToArray());
            }
        }
    }

    [Fact]
    public async Task ValidityBitmapsSeparateNullFromEmpty()
    {
        var rows = new List<ArrowNullableExportRecord>
        {
            new()
            {
                Id = 0,
                MaybeInt = 42,
                MaybeFlag = true,
                MaybeName = "present",
                MaybePrice = 1.234m,
                MaybeWhen = BaseInstant,
                MaybeGuid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                MaybePayload = new byte[] { 1, 2, 3 },
            },
            new()
            {
                Id = 1,
                MaybeInt = null,
                MaybeFlag = null,
                MaybeName = null,
                MaybePrice = null,
                MaybeWhen = null,
                MaybeGuid = null,
                MaybePayload = null,
            },
            new()
            {
                Id = 2,
                MaybeInt = 0,
                MaybeFlag = false,
                MaybeName = string.Empty,
                MaybePrice = 0m,
                MaybeWhen = BaseInstant,
                MaybeGuid = Guid.Empty,
                MaybePayload = System.Array.Empty<byte>(),
            },
        };

        using var stream = new MemoryStream();
        await rows.WriteParquetAsync(stream);
        stream.Position = 0;

        RecordBatch? batch = null;
        await foreach (
            RecordBatch b in ArrowNullableExportRecordParquetExtensions.ReadParquetRecordBatchesAsync(
                stream
            )
        )
        {
            Assert.Null(batch);
            batch = b;
        }

        Assert.NotNull(batch);
        using RecordBatch materialised = batch!;

        var ints = (Int32Array)materialised.Column("maybe_int");
        var flags = (BooleanArray)materialised.Column("maybe_flag");
        var names = (StringArray)materialised.Column("maybe_name");
        var prices = (Decimal128Array)materialised.Column("maybe_price");
        var whens = (TimestampArray)materialised.Column("maybe_when");
        var guids = (Apache.Arrow.Arrays.FixedSizeBinaryArray)materialised.Column("maybe_guid");
        var payloads = (BinaryArray)materialised.Column("maybe_payload");

        // Row 0: everything present.
        Assert.Equal(42, ints.GetValue(0));
        Assert.True(flags.GetValue(0));
        Assert.Equal("present", names.GetString(0));

        // Row 1: everything null — validity, not a sentinel value.
        Assert.True(ints.IsNull(1));
        Assert.True(flags.IsNull(1));
        Assert.True(names.IsNull(1));
        Assert.Null(names.GetString(1));
        Assert.True(prices.IsNull(1));
        Assert.True(whens.IsNull(1));
        Assert.True(guids.IsNull(1));
        Assert.True(payloads.IsNull(1));

        // Row 2: present-but-empty/zero must stay distinct from null.
        Assert.False(ints.IsNull(2));
        Assert.Equal(0, ints.GetValue(2));
        Assert.False(names.IsNull(2));
        Assert.Equal(string.Empty, names.GetString(2));
        Assert.False(payloads.IsNull(2));
        Assert.Empty(payloads.GetBytes(2).ToArray());
        Assert.False(guids.IsNull(2));
        Assert.Equal(UuidBytes(Guid.Empty), guids.GetBytes(2).ToArray());

        Assert.Equal(1, ints.NullCount);
        Assert.Equal(1, names.NullCount);
        Assert.Equal(1, payloads.NullCount);
    }

    [Fact]
    public void ArrowSchemaCarriesPrecisionScaleAndUnits()
    {
        Apache.Arrow.Schema schema = ArrowExportRecordParquetExtensions.ArrowSchema;

        Assert.Equal(16, schema.FieldsList.Count);

        var price = Assert.IsType<Decimal128Type>(schema.GetFieldByName("price").DataType);
        Assert.Equal(18, price.Precision);
        Assert.Equal(4, price.Scale);

        var created = Assert.IsType<TimestampType>(schema.GetFieldByName("created_at").DataType);
        Assert.Equal(TimeUnit.Microsecond, created.Unit);

        var clock = Assert.IsType<Time64Type>(schema.GetFieldByName("clock").DataType);
        Assert.Equal(TimeUnit.Microsecond, clock.Unit);

        Assert.IsType<DurationType>(schema.GetFieldByName("elapsed").DataType);
        Assert.IsType<Date32Type>(schema.GetFieldByName("day").DataType);

        var uuid = Assert.IsType<FixedSizeBinaryType>(
            schema.GetFieldByName("correlation_id").DataType
        );
        Assert.Equal(16, uuid.ByteWidth);

        Assert.False(schema.GetFieldByName("id").IsNullable);
        Assert.True(schema.GetFieldByName("name").IsNullable);
    }

    [Fact]
    public void NullableSchemaPrecisionComesFromTheDecimalAnnotation()
    {
        Apache.Arrow.Schema schema = ArrowNullableExportRecordParquetExtensions.ArrowSchema;
        var price = Assert.IsType<Decimal128Type>(schema.GetFieldByName("maybe_price").DataType);
        Assert.Equal(12, price.Precision);
        Assert.Equal(3, price.Scale);
        Assert.True(schema.GetFieldByName("maybe_price").IsNullable);
    }

    [Fact]
    public async Task ExportedBatchesSurviveAnArrowIpcRoundTrip()
    {
        // Independent validation of the buffer layout: the IPC writer and reader are Arrow's own
        // code, not this repo's, and they walk offsets, validity bitmaps and buffer alignment. A
        // batch with a malformed bitmap or a bad offset chain does not survive the trip.
        List<ArrowExportRecord> rows = SampleRows(6);
        using MemoryStream parquet = await WriteAsync(rows, rowGroupSize: 4);
        List<RecordBatch> exported = await CollectAsync(parquet, maxRowsPerBatch: 3);

        using var ipc = new MemoryStream();
        using (
            var writer = new Apache.Arrow.Ipc.ArrowFileWriter(
                ipc,
                ArrowExportRecordParquetExtensions.ArrowSchema,
                leaveOpen: true
            )
        )
        {
            await writer.WriteStartAsync();
            foreach (RecordBatch batch in exported)
            {
                await writer.WriteRecordBatchAsync(batch);
                batch.Dispose();
            }

            await writer.WriteEndAsync();
        }

        ipc.Position = 0;
        using var reader = new Apache.Arrow.Ipc.ArrowFileReader(ipc);
        var readBack = new List<string?>();
        int total = 0;
        for (int i = 0; i < await reader.RecordBatchCountAsync(); i++)
        {
            using RecordBatch batch = await reader.ReadRecordBatchAsync(i);
            total += batch.Length;
            var names = (StringArray)batch.Column("name");
            for (int r = 0; r < names.Length; r++)
            {
                readBack.Add(names.GetString(r));
            }
        }

        Assert.Equal(rows.Count, total);
        Assert.Equal(rows.Select(r => r.Name).ToArray(), readBack.ToArray());
    }

    [Fact]
    public async Task IntervalColumnsExportAsMonthDayNanosecondIntervals()
    {
        var rows = new List<ArrowIntervalRecord>
        {
            new()
            {
                Id = 0,
                Span = new global::Parquet.File.Values.Primitives.Interval(1, 2, 3_000),
            },
            new() { Id = 1, Span = new global::Parquet.File.Values.Primitives.Interval(0, 0, 0) },
        };

        using var stream = new MemoryStream();
        await rows.WriteParquetAsync(stream);
        stream.Position = 0;

        List<RecordBatch> batches = new();
        await foreach (
            RecordBatch b in ArrowIntervalRecordParquetExtensions.ReadParquetRecordBatchesAsync(
                stream
            )
        )
        {
            batches.Add(b);
        }

        using RecordBatch batch = Assert.Single(batches);
        var spans = (MonthDayNanosecondIntervalArray)batch.Column("span");

        Apache.Arrow.Scalars.MonthDayNanosecondInterval first = spans.GetValue(0)!.Value;
        Assert.Equal(1, first.Months);
        Assert.Equal(2, first.Days);
        Assert.Equal(3_000_000_000L, first.Nanoseconds);

        Apache.Arrow.Scalars.MonthDayNanosecondInterval second = spans.GetValue(1)!.Value;
        Assert.Equal(0, second.Months);
        Assert.Equal(0, second.Days);
        Assert.Equal(0L, second.Nanoseconds);

        Assert.IsType<IntervalType>(
            ArrowIntervalRecordParquetExtensions.ArrowSchema.GetFieldByName("span").DataType
        );
    }

    [Fact]
    public async Task EmptyFileYieldsNoBatches()
    {
        using var stream = new MemoryStream();
        await new List<ArrowExportRecord>().WriteParquetAsync(stream);
        stream.Position = 0;

        List<RecordBatch> batches = await CollectAsync(stream);
        Assert.Empty(batches);
    }

    [Fact]
    public async Task NullStreamIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (
                RecordBatch _ in ArrowExportRecordParquetExtensions.ReadParquetRecordBatchesAsync(
                    null!
                )
            )
            {
                // Never reached.
            }
        });
    }

    private static byte[] UuidBytes(Guid value)
    {
        byte[] bytes = value.ToByteArray();
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
        return bytes;
    }

    // ──────────────────────────────────────────────────────────
    //  CONDITIONAL EMISSION GATE
    // ──────────────────────────────────────────────────────────

    private const string GateSource = """
        using Parquet.SourceGenerator;
        using System;

        namespace GateDomain;

        [ParquetSerializable]
        public partial record GateRow
        {
            public int Id { get; init; }
            public string? Name { get; init; }
        }
        """;

    private const string NestedSource = """
        using Parquet.SourceGenerator;
        using System;

        namespace GateDomain;

        [ParquetSerializable]
        public partial class GateAddress
        {
            public string? City { get; init; }
        }

        [ParquetSerializable]
        public partial record GateNestedRow
        {
            public int Id { get; init; }
            public GateAddress? Ship { get; init; }
        }
        """;

    [Fact]
    public void NoArrowReferenceEmitsNoArrowTypedCode()
    {
        Dictionary<string, string> emitted = RunGenerator(GateSource, withArrow: false);

        Assert.DoesNotContain(
            emitted.Keys,
            n => n.EndsWith(".Arrow.g.cs", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            "Apache.Arrow",
            string.Join("\n", emitted.Values),
            StringComparison.Ordinal
        );
        Assert.Contains(
            emitted.Keys,
            n => n.EndsWith(".ParquetSerializer.g.cs", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ArrowReferenceAddsOnlyTheGatedFile()
    {
        Dictionary<string, string> without = RunGenerator(GateSource, withArrow: false);
        Dictionary<string, string> with = RunGenerator(GateSource, withArrow: true);

        Assert.Contains(with.Keys, n => n.EndsWith(".Arrow.g.cs", StringComparison.Ordinal));
        Assert.Equal(without.Count + 1, with.Count);

        // Flipping the reference re-runs only the gated output: every other file is byte-identical.
        foreach (KeyValuePair<string, string> file in without)
        {
            Assert.True(with.ContainsKey(file.Key), $"missing {file.Key} after the reference flip");
            Assert.Equal(file.Value, with[file.Key]);
        }
    }

    [Fact]
    public void NestedModelsGetNoArrowBridgeYet()
    {
        Dictionary<string, string> emitted = RunGenerator(NestedSource, withArrow: true);

        // GateAddress is flat and exportable; the nested row is not, pending #176.
        Assert.Contains(
            emitted.Keys,
            n => n.EndsWith("GateAddress.Arrow.g.cs", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            emitted.Keys,
            n => n.EndsWith("GateNestedRow.Arrow.g.cs", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ExportPathNeverConstructsThePoco()
    {
        Dictionary<string, string> emitted = RunGenerator(GateSource, withArrow: true);
        string arrow = Assert.Single(
            emitted
                .Where(kv => kv.Key.EndsWith(".Arrow.g.cs", StringComparison.Ordinal))
                .Select(kv => kv.Value)
        );

        // The whole point of the bridge: column buffers go straight to Arrow, so the emitted
        // export file never allocates a row object.
        Assert.DoesNotContain("new GateRow", arrow, StringComparison.Ordinal);
        Assert.DoesNotContain("GateDomain.GateRow", arrow, StringComparison.Ordinal);
        Assert.Contains("ReadParquetRecordBatchesAsync", arrow, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> RunGenerator(string source, bool withArrow)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(ParquetSerializableAttribute).Assembly.Location
            ),
            MetadataReference.CreateFromFile(typeof(Parquet.ParquetReader).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
        };

        if (withArrow)
        {
            references.Add(MetadataReference.CreateFromFile(typeof(RecordBatch).Assembly.Location));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ArrowGateAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var generator = new ParquetIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);

        GeneratorRunResult run = driver.GetRunResult().Results.Single();
        return run.GeneratedSources.ToDictionary(
            s => s.HintName,
            s => s.SourceText.ToString(),
            StringComparer.Ordinal
        );
    }
}
