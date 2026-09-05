// Native AOT regression matrix.
//
// The purpose is narrow and specific: prove the *whole* serialization surface still works when
// compiled ahead of time, so a dependency upgrade that reintroduces reflection into a core path is
// caught here rather than by a user.
//
// Why it has to execute rather than merely compile. Reflection that AOT cannot resolve does not
// reliably fail the build — trimming and AOT analysis emit warnings, and Parquet.Net already emits
// some (IL2104, IL3053), so the build cannot be gated on their absence. What AOT does instead is
// fail at *runtime*: MissingMetadataException, NotSupportedException, or a silently defaulted
// value. So every supported type and every generated API path has to be exercised for real, and its
// results checked, in a natively compiled binary.
//
// Why it is not an xunit suite. A test runner discovers and invokes tests by reflection, which is
// the very thing under test, and the runner's own AOT problems would be indistinguishable from the
// library's. This is a plain executable with an explicit list of checks, so a failure is
// attributable to the code being verified.
//
// Coverage is organised by *kind* rather than by API, because the reflection risk lives in how a
// value is converted: every PropertyKind the generator emits (Primitive, Decimal, DateTime,
// TimeSpan, Guid, Enum, ByteArray), in nullable and non-nullable form with nulls actually present,
// across every read/write entry point and every compression codec.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Parquet.SourceGenerator;

namespace Parquet.SourceGenerator.AotTest;

public enum AotStatus
{
    Pending = 0,
    Active = 1,
    Closed = 2,
}

/// <summary>Every non-nullable PropertyKind the generator supports, in one model.</summary>
[ParquetSerializable]
public sealed partial record AotWideRecord
{
    [ParquetColumn("f_int", Order = 1)]
    public int Int32Value { get; init; }

    [ParquetColumn("f_long", Order = 2)]
    public long Int64Value { get; init; }

    [ParquetColumn("f_double", Order = 3)]
    public double DoubleValue { get; init; }

    [ParquetColumn("f_float", Order = 4)]
    public float FloatValue { get; init; }

    [ParquetColumn("f_bool", Order = 5)]
    public bool BoolValue { get; init; }

    [ParquetColumn("f_string", Order = 6)]
    public string StringValue { get; init; } = string.Empty;

    [ParquetColumn("f_decimal", Order = 7)]
    [ParquetDecimal(18, 4)]
    public decimal DecimalValue { get; init; }

    [ParquetColumn("f_datetime", Order = 8)]
    public DateTime DateTimeValue { get; init; }

    [ParquetColumn("f_datetime_micros", Order = 9)]
    [ParquetTimestamp(ParquetTimestampUnit.Microseconds)]
    public DateTime MicrosecondValue { get; init; }

    [ParquetColumn("f_timespan", Order = 10)]
    public TimeSpan TimeSpanValue { get; init; }

    [ParquetColumn("f_guid", Order = 11)]
    public Guid GuidValue { get; init; }

    [ParquetColumn("f_enum", Order = 12)]
    public AotStatus EnumValue { get; init; }

    [ParquetColumn("f_bytes", Order = 13)]
    public byte[] ByteArrayValue { get; init; } = Array.Empty<byte>();

    [ParquetIgnore]
    public string NotPersisted { get; init; } = "ignored";
}

/// <summary>The same kinds again, nullable — exercised with real nulls present.</summary>
[ParquetSerializable]
public sealed partial record AotNullableRecord
{
    [ParquetColumn("n_id", Order = 1)]
    public int Id { get; init; }

    [ParquetColumn("n_int", Order = 2)]
    public int? Int32Value { get; init; }

    [ParquetColumn("n_long", Order = 3)]
    public long? Int64Value { get; init; }

    [ParquetColumn("n_double", Order = 4)]
    public double? DoubleValue { get; init; }

    [ParquetColumn("n_bool", Order = 5)]
    public bool? BoolValue { get; init; }

    [ParquetColumn("n_string", Order = 6)]
    public string? StringValue { get; init; }

    [ParquetColumn("n_datetime", Order = 7)]
    public DateTime? DateTimeValue { get; init; }

    [ParquetColumn("n_timespan", Order = 8)]
    public TimeSpan? TimeSpanValue { get; init; }

    [ParquetColumn("n_guid", Order = 9)]
    public Guid? GuidValue { get; init; }

    [ParquetColumn("n_enum", Order = 10)]
    public AotStatus? EnumValue { get; init; }
}

/// <summary>
/// Three of AotWideRecord's columns declared in the opposite order, so a file written by one is
/// read by the other through the name-matching resolution path.
/// </summary>
[ParquetSerializable]
public sealed partial record AotReorderedRecord
{
    [ParquetColumn("f_double", Order = 1)]
    public double DoubleValue { get; init; }

    [ParquetColumn("f_long", Order = 2)]
    public long Int64Value { get; init; }

    [ParquetColumn("f_int", Order = 3)]
    public int Int32Value { get; init; }
}

[ParquetSerializable]
public sealed partial record AotNarrowRecord
{
    [ParquetColumn("id", Order = 1)]
    public int Id { get; init; }

    [ParquetColumn("label", Order = 2)]
    public string Label { get; init; } = string.Empty;
}

internal static class Program
{
    private static int _failures;
    private static int _passes;

    // Parquet.Net 6.1.0 maps string and byte[] columns to physical struct types ReadOnlyMemory<char>
    // and ReadOnlyMemory<byte>, and calls Type.MakeGenericType(typeof(Nullable<>), ...) when building
    // nullable DataFields. In Native AOT, MakeGenericType over value types requires static presence
    // of the instantiated type handle.
    private static readonly Type[] _aotPreservedTypes =
    [
        typeof(ReadOnlyMemory<char>?),
        typeof(ReadOnlyMemory<byte>?),
    ];

    private static async Task<int> Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("Native AOT regression matrix");
        Console.WriteLine("=================================================");

        await CheckAsync("schema construction (static, all kinds)", SchemaConstructionAsync);
        await CheckAsync("all property kinds round-trip", WideRecordRoundtripAsync);
        await CheckAsync("nullable kinds round-trip with nulls present", NullableRoundtripAsync);
        await CheckAsync("[ParquetIgnore] member is not persisted", IgnoredMemberAsync);
        await CheckAsync(
            "microsecond timestamps keep sub-millisecond precision",
            MicrosecondPrecisionAsync
        );
        await CheckAsync("batched write produces multiple row groups", BatchedWriteAsync);
        await CheckAsync("parallel read across row groups", ParallelReadAsync);
        await CheckAsync("read from ReadOnlyMemory<byte>", MemoryReadAsync);
        await CheckAsync("IAsyncEnumerable streaming write", AsyncEnumerableWriteAsync);
        await CheckAsync(
            "schema field resolution by name (reordered columns)",
            ReorderedColumnsAsync
        );
        await CheckAsync("every compression codec round-trips", CompressionCodecsAsync);

        Console.WriteLine("=================================================");
        if (_failures == 0)
        {
            Console.WriteLine($"All {_passes} AOT checks passed.");
            Console.WriteLine("=================================================");
            return 0;
        }

        Console.WriteLine($"{_failures} of {_passes + _failures} AOT checks FAILED.");
        Console.WriteLine(
            "A failure here usually means a code path started depending on reflection,"
        );
        Console.WriteLine(
            "on metadata the trimmer removed, or on dynamic loading AOT cannot honour."
        );
        Console.WriteLine("=================================================");
        return 1;
    }

    private static async Task CheckAsync(string name, Func<Task> body)
    {
        try
        {
            await body();
            _passes++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            // The exception *type* is the diagnostic signal: MissingMetadataException or
            // NotSupportedException points at AOT/trimming, anything else is an ordinary bug.
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"          {ex}");
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static AotWideRecord SampleWide(int seed) =>
        new()
        {
            Int32Value = seed,
            Int64Value = seed * 1_000_000_000L,
            DoubleValue = seed + 0.5,
            FloatValue = seed + 0.25f,
            BoolValue = seed % 2 == 0,
            StringValue = $"row-{seed}",
            DecimalValue = new decimal(seed) + 0.1234m,
            // Whole milliseconds: the default DateTime column is not microsecond-encoded.
            DateTimeValue = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(
                seed
            ),
            MicrosecondValue = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(
                (seed * 10) + 70
            ),
            TimeSpanValue = TimeSpan.FromMilliseconds(seed * 37),
            GuidValue = new Guid(seed, 0x1234, 0x5678, 1, 2, 3, 4, 5, 6, 7, 8),
            EnumValue = (AotStatus)(seed % 3),
            ByteArrayValue = new byte[] { (byte)seed, 0xAA, 0xBB },
        };

    private static void ExpectWideEqual(AotWideRecord expected, AotWideRecord actual, int index)
    {
        Expect(actual.Int32Value == expected.Int32Value, $"row {index}: int mismatch");
        Expect(actual.Int64Value == expected.Int64Value, $"row {index}: long mismatch");
        Expect(actual.DoubleValue == expected.DoubleValue, $"row {index}: double mismatch");
        Expect(actual.FloatValue == expected.FloatValue, $"row {index}: float mismatch");
        Expect(actual.BoolValue == expected.BoolValue, $"row {index}: bool mismatch");
        Expect(actual.StringValue == expected.StringValue, $"row {index}: string mismatch");
        Expect(
            actual.DecimalValue == expected.DecimalValue,
            $"row {index}: decimal mismatch — expected {expected.DecimalValue}, got {actual.DecimalValue}"
        );
        Expect(
            actual.DateTimeValue == expected.DateTimeValue,
            $"row {index}: DateTime mismatch — expected {expected.DateTimeValue:O}, got {actual.DateTimeValue:O}"
        );
        Expect(actual.TimeSpanValue == expected.TimeSpanValue, $"row {index}: TimeSpan mismatch");
        Expect(
            actual.GuidValue == expected.GuidValue,
            $"row {index}: Guid mismatch — expected {expected.GuidValue}, got {actual.GuidValue}"
        );
        Expect(actual.EnumValue == expected.EnumValue, $"row {index}: enum mismatch");

        Expect(
            actual.ByteArrayValue.Length == expected.ByteArrayValue.Length,
            $"row {index}: byte[] length mismatch"
        );
        for (int b = 0; b < expected.ByteArrayValue.Length; b++)
        {
            Expect(
                actual.ByteArrayValue[b] == expected.ByteArrayValue[b],
                $"row {index}: byte[{b}] mismatch"
            );
        }
    }

    private static Task SchemaConstructionAsync()
    {
        // The schema is a static readonly built at type-init, so a construction failure surfaces as
        // a TypeInitializationException the first time the class is touched. Reading it before
        // anything else keeps that distinguishable from a serialization fault.
        Expect(
            AotWideRecordParquetExtensions.Schema.Fields.Count == 13,
            $"expected 13 schema fields, found {AotWideRecordParquetExtensions.Schema.Fields.Count}"
        );
        Expect(
            AotNullableRecordParquetExtensions.Schema.Fields.Count == 10,
            $"expected 10 nullable schema fields, found {AotNullableRecordParquetExtensions.Schema.Fields.Count}"
        );
        Expect(
            AotWideRecordParquetExtensions.Schema.Fields[0].Name == "f_int",
            "schema field order does not follow the declared Order"
        );
        return Task.CompletedTask;
    }

    private static async Task WideRecordRoundtripAsync()
    {
        var written = new List<AotWideRecord>();
        for (int i = 0; i < 25; i++)
        {
            written.Add(SampleWide(i));
        }

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        Expect(stream.Length > 0, "write produced an empty stream");

        stream.Position = 0;
        List<AotWideRecord> read = await AotWideRecordParquetExtensions.ReadParquetAsync(stream);

        Expect(read.Count == written.Count, $"expected {written.Count} rows, read {read.Count}");
        for (int i = 0; i < written.Count; i++)
        {
            ExpectWideEqual(written[i], read[i], i);
        }
    }

    private static async Task NullableRoundtripAsync()
    {
        // Alternating populated and all-null rows: a converter that loses nullability under AOT
        // typically returns a default instead of null, which the null row below catches.
        var written = new List<AotNullableRecord>
        {
            new()
            {
                Id = 1,
                Int32Value = 42,
                Int64Value = 43L,
                DoubleValue = 44.5,
                BoolValue = true,
                StringValue = "present",
                DateTimeValue = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                TimeSpanValue = TimeSpan.FromSeconds(90),
                GuidValue = new Guid("11112222-3333-4444-5555-666677778888"),
                EnumValue = AotStatus.Active,
            },
            new() { Id = 2 },
            new()
            {
                Id = 3,
                Int32Value = -7,
                StringValue = null,
                EnumValue = AotStatus.Closed,
            },
        };

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        stream.Position = 0;
        List<AotNullableRecord> read = await AotNullableRecordParquetExtensions.ReadParquetAsync(
            stream
        );

        Expect(read.Count == 3, $"expected 3 rows, read {read.Count}");

        Expect(read[0].Int32Value == 42, "row 0: int? value lost");
        Expect(read[0].Int64Value == 43L, "row 0: long? value lost");
        Expect(read[0].DoubleValue == 44.5, "row 0: double? value lost");
        Expect(read[0].BoolValue == true, "row 0: bool? value lost");
        Expect(read[0].StringValue == "present", "row 0: string? value lost");
        Expect(read[0].TimeSpanValue == TimeSpan.FromSeconds(90), "row 0: TimeSpan? value lost");
        Expect(
            read[0].GuidValue == new Guid("11112222-3333-4444-5555-666677778888"),
            "row 0: Guid? value lost"
        );
        Expect(read[0].EnumValue == AotStatus.Active, "row 0: enum? value lost");

        Expect(read[1].Int32Value is null, "row 1: int? should be null, got a value");
        Expect(read[1].Int64Value is null, "row 1: long? should be null");
        Expect(read[1].DoubleValue is null, "row 1: double? should be null");
        Expect(read[1].BoolValue is null, "row 1: bool? should be null");
        Expect(read[1].StringValue is null, "row 1: string? should be null");
        Expect(read[1].DateTimeValue is null, "row 1: DateTime? should be null");
        Expect(read[1].TimeSpanValue is null, "row 1: TimeSpan? should be null");
        Expect(read[1].GuidValue is null, "row 1: Guid? should be null");
        Expect(read[1].EnumValue is null, "row 1: enum? should be null");

        Expect(read[2].Int32Value == -7, "row 2: negative int? lost");
        Expect(read[2].StringValue is null, "row 2: explicit null string should stay null");
        Expect(read[2].EnumValue == AotStatus.Closed, "row 2: enum? value lost");
    }

    private static async Task IgnoredMemberAsync()
    {
        var written = new List<AotWideRecord>
        {
            SampleWide(1) with
            {
                NotPersisted = "should not survive",
            },
        };

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        stream.Position = 0;
        List<AotWideRecord> read = await AotWideRecordParquetExtensions.ReadParquetAsync(stream);

        Expect(
            read[0].NotPersisted == "ignored",
            $"[ParquetIgnore] member should come back at its initializer, got '{read[0].NotPersisted}'"
        );
    }

    private static async Task MicrosecondPrecisionAsync()
    {
        AotWideRecord sample = SampleWide(3);
        Expect(
            sample.MicrosecondValue.Ticks % TimeSpan.TicksPerMillisecond != 0,
            "test value must carry a sub-millisecond component to be meaningful"
        );

        using var stream = new MemoryStream();
        await new List<AotWideRecord> { sample }.WriteParquetAsync(stream);
        stream.Position = 0;
        List<AotWideRecord> read = await AotWideRecordParquetExtensions.ReadParquetAsync(stream);

        const long ticksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;
        long expected = sample.MicrosecondValue.Ticks / ticksPerMicrosecond;
        long actual = read[0].MicrosecondValue.Ticks / ticksPerMicrosecond;
        Expect(
            expected == actual,
            $"microsecond timestamp lost precision — expected {expected}us, got {actual}us"
        );
    }

    private static async Task BatchedWriteAsync()
    {
        var written = new List<AotWideRecord>();
        for (int i = 0; i < 120; i++)
        {
            written.Add(SampleWide(i));
        }

        using var stream = new MemoryStream();
        await written.WriteParquetBatchedAsync(stream, rowGroupSize: 25);
        stream.Position = 0;
        List<AotWideRecord> read = await AotWideRecordParquetExtensions.ReadParquetAsync(stream);

        Expect(read.Count == 120, $"expected 120 rows across row groups, read {read.Count}");
        ExpectWideEqual(written[0], read[0], 0);
        ExpectWideEqual(written[119], read[119], 119);
    }

    private static async Task ParallelReadAsync()
    {
        var written = new List<AotWideRecord>();
        for (int i = 0; i < 120; i++)
        {
            written.Add(SampleWide(i));
        }

        using var stream = new MemoryStream();
        await written.WriteParquetBatchedAsync(stream, rowGroupSize: 20);
        stream.Position = 0;

        // Exercises the threaded path as well as the converters, since row groups decode
        // concurrently.
        List<AotWideRecord> read = await AotWideRecordParquetExtensions.ReadParquetParallelAsync(
            stream,
            maxDegreeOfParallelism: 4
        );

        Expect(read.Count == 120, $"expected 120 rows from the parallel reader, read {read.Count}");
        for (int i = 0; i < written.Count; i++)
        {
            // Ordering matters: row groups decode concurrently but must be reassembled in order.
            ExpectWideEqual(written[i], read[i], i);
        }
    }

    private static async Task MemoryReadAsync()
    {
        var written = new List<AotWideRecord> { SampleWide(9), SampleWide(10) };

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        ReadOnlyMemory<byte> buffer = stream.ToArray();

        List<AotWideRecord> read = await AotWideRecordParquetExtensions.ReadParquetAsync(buffer);

        Expect(read.Count == 2, $"expected 2 rows from the buffer overload, read {read.Count}");
        ExpectWideEqual(written[0], read[0], 0);
        ExpectWideEqual(written[1], read[1], 1);
    }

    private static async IAsyncEnumerable<AotNarrowRecord> StreamRecordsAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return new AotNarrowRecord { Id = i, Label = $"streamed-{i}" };
        }
    }

    private static async Task AsyncEnumerableWriteAsync()
    {
        using var stream = new MemoryStream();
        await StreamRecordsAsync(60).WriteParquetAsync(stream, rowGroupSize: 16);
        stream.Position = 0;

        List<AotNarrowRecord> read = await AotNarrowRecordParquetExtensions.ReadParquetAsync(
            stream
        );

        Expect(read.Count == 60, $"expected 60 streamed rows, read {read.Count}");
        Expect(read[0].Label == "streamed-0", "first streamed row is wrong");
        Expect(read[59].Label == "streamed-59", "last streamed row is wrong");
    }

    private static async Task ReorderedColumnsAsync()
    {
        // Forces the name-matching fallback, which builds a Dictionary with an OrdinalIgnoreCase
        // comparer — string comparers and hashing are a plausible casualty of trimming.
        var written = new List<AotWideRecord> { SampleWide(5) };

        using var stream = new MemoryStream();
        await written.WriteParquetAsync(stream);
        stream.Position = 0;

        List<AotReorderedRecord> read = await AotReorderedRecordParquetExtensions.ReadParquetAsync(
            stream
        );

        Expect(read.Count == 1, $"expected 1 row, read {read.Count}");
        Expect(
            read[0].Int32Value == written[0].Int32Value,
            "columns resolved positionally instead of by name — int is wrong"
        );
        Expect(read[0].Int64Value == written[0].Int64Value, "reordered long mismatch");
        Expect(read[0].DoubleValue == written[0].DoubleValue, "reordered double mismatch");
    }

    private static async Task CompressionCodecsAsync()
    {
        // Codecs are the most likely place for a dependency to start loading something dynamically,
        // and a codec that fails under AOT would otherwise only be found in production.
        ParquetCompressionMethod[] methods =
        {
            ParquetCompressionMethod.None,
            ParquetCompressionMethod.Snappy,
            ParquetCompressionMethod.Gzip,
            ParquetCompressionMethod.Lz4,
            ParquetCompressionMethod.Brotli,
            ParquetCompressionMethod.Zstd,
        };

        for (int m = 0; m < methods.Length; m++)
        {
            ParquetCompressionMethod method = methods[m];
            var written = new List<AotWideRecord> { SampleWide(1), SampleWide(2) };

            using var stream = new MemoryStream();
            await written.WriteParquetAsync(
                stream,
                new ParquetSerializerOptions { CompressionMethod = method }
            );
            Expect(stream.Length > 0, $"{method}: write produced an empty stream");

            stream.Position = 0;
            List<AotWideRecord> read = await AotWideRecordParquetExtensions.ReadParquetAsync(
                stream
            );

            Expect(read.Count == 2, $"{method}: expected 2 rows, read {read.Count}");
            ExpectWideEqual(written[0], read[0], 0);
            ExpectWideEqual(written[1], read[1], 1);
        }
    }
}
