using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Parquet;
using Parquet.Meta;
using Parquet.Schema;
using Shouldly;
using Xunit;
using ParquetPhysicalType = Parquet.Meta.Type;

namespace Parquet.SourceGenerator.Tests.Security;

public class HostileParquetTests
{
    private static readonly int[] SingleOneArray = [1];
    private static readonly int[] SingleZeroArray = [0];
    private static readonly int[] SingleNinetyNineArray = [99];
    private static readonly int[] SingleHundredArray = [100];
    private static readonly int[] TwoZerosArray = [0, 0];
    private static readonly int[] TwoTwosArray = [2, 2];
    private static readonly int[] TwoValuesArray = [100, 200];

    [Fact]
    public async Task RowGroupCountExceedsMaxThrowsInvalidDataException()
    {
        // Generate a valid file with 3 row groups (each size 2)
        var items = Enumerable
            .Range(0, 6)
            .Select(i => new MultiRowGroupModel { Id = i, Name = $"item_{i}" })
            .ToList();

        using var ms = new MemoryStream();
        await items.WriteParquetBatchedAsync(ms, new ParquetSerializerOptions { RowGroupSize = 2 });

        var hostileOptions = new ParquetSerializerOptions
        {
            MaxRowGroupCount = 2, // File has 3 row groups, which exceeds the limit of 2
        };

        // 1. ReadParquetAsync (List)
        ms.Position = 0;
        var exList = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetAsync(ms, hostileOptions)
        );
        exList.Message.ShouldContain("Row group count");
        exList.Message.ShouldContain("exceeds maximum allowed");

        // 2. ReadParquetArrayAsync (Array)
        ms.Position = 0;
        var exArr = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetArrayAsync(ms, hostileOptions)
        );
        exArr.Message.ShouldContain("Row group count");

        // 3. ReadParquetParallelArrayAsync (Parallel Array)
        ms.Position = 0;
        var exParallel = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetParallelArrayAsync(ms, hostileOptions)
        );
        exParallel.Message.ShouldContain("Row group count");

        // 4. ReadParquetStreamAsync (IAsyncEnumerable)
        ms.Position = 0;
        var exStream = await Should.ThrowAsync<InvalidDataException>(async () =>
        {
            await foreach (
                var _ in MultiRowGroupModelParquetExtensions.ReadParquetStreamAsync(
                    ms,
                    hostileOptions
                )
            ) { }
        });
        exStream.Message.ShouldContain("Row group count");
    }

    [Fact]
    public async Task TotalRowCountExceedsMaxAllocationValuesThrowsInvalidDataException()
    {
        // 10 items total
        var items = Enumerable
            .Range(0, 10)
            .Select(i => new MultiRowGroupModel { Id = i, Name = $"item_{i}" })
            .ToList();

        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);

        var hostileOptions = new ParquetSerializerOptions
        {
            MaxAllocationValues = 5, // File has 10 rows, which exceeds limit of 5
        };

        // 1. ReadParquetAsync
        ms.Position = 0;
        var exList = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetAsync(ms, hostileOptions)
        );
        exList.Message.ShouldContain("exceeds maximum allowed");

        // 2. ReadParquetArrayAsync
        ms.Position = 0;
        var exArr = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetArrayAsync(ms, hostileOptions)
        );
        exArr.Message.ShouldContain("exceeds maximum allowed");

        // 3. ReadParquetParallelArrayAsync
        ms.Position = 0;
        var exParallel = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetParallelArrayAsync(ms, hostileOptions)
        );
        exParallel.Message.ShouldContain("exceeds maximum allowed");

        // 4. ReadParquetStreamAsync (stream checks each row group row count against MaxAllocationValues)
        ms.Position = 0;
        var exStream = await Should.ThrowAsync<InvalidDataException>(async () =>
        {
            await foreach (
                var _ in MultiRowGroupModelParquetExtensions.ReadParquetStreamAsync(
                    ms,
                    hostileOptions
                )
            ) { }
        });
        exStream.Message.ShouldContain("exceeds maximum allowed");
    }

    [Fact]
    public async Task SchemaNestingDepthExceedsMaxThrowsInvalidDataException()
    {
        // NestedOrder has depth 2 (Address struct in NestedOrder)
        var items = new List<NestedOrder>
        {
            new()
            {
                Id = 1,
                Ship = new Address { City = "Seattle", Zip = 98101 },
                Bill = new Address { City = "Redmond", Zip = 98052 },
            },
        };

        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);

        var hostileOptions = new ParquetSerializerOptions
        {
            MaxNestingDepth = 1, // NestedOrder has depth 2 > 1
        };

        ms.Position = 0;
        var ex = await Should.ThrowAsync<InvalidDataException>(() =>
            NestedOrderParquetExtensions.ReadParquetAsync(ms, hostileOptions)
        );
        ex.Message.ShouldContain("Schema nesting depth");
        ex.Message.ShouldContain("exceeds maximum allowed 1");
    }

    [Fact]
    public async Task ListColumnValuesExceedMaxAllocationValuesThrowsInvalidDataException()
    {
        // ListRow with multiple list elements
        var items = new List<ListRow>
        {
            new()
            {
                Id = 1,
                Scores = new List<int> { 10, 20, 30, 40, 50 },
                Tags = new List<string?> { "a", "b", "c", "d" },
                Keys = [Guid.NewGuid(), Guid.NewGuid()],
            },
        };

        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);

        // Limit allocation values to 3, but Scores has 5 values
        var hostileOptions = new ParquetSerializerOptions { MaxAllocationValues = 3 };

        ms.Position = 0;
        var ex = await Should.ThrowAsync<InvalidDataException>(() =>
            ListRowParquetExtensions.ReadParquetAsync(ms, hostileOptions)
        );
        ex.Message.ShouldContain("exceeds maximum allowed 3");
    }

    private static readonly int[] SingleNegativeOneArray = [-1];

    [Fact]
    public async Task RepetitionLevelRowOverrunThrowsInvalidDataException()
    {
        var items = new List<ListRow>
        {
            new() { Id = 1, Scores = [10] },
            new() { Id = 2, Scores = [20] },
        };
        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);

        byte[] bytes = ms.ToArray();
        int footerLength = BitConverter.ToInt32(bytes, bytes.Length - 8);
        int footerStart = bytes.Length - 4 - footerLength;
        var offsets = new List<int>();
        for (int i = footerStart; i < bytes.Length - 5; i++)
        {
            if (bytes[i] == 0x16 && bytes[i + 1] == 0x04)
            {
                offsets.Add(i);
            }
        }

        (offsets.Count >= 3).ShouldBeTrue(
            "Expected at least 3 num_rows/num_values occurrences in Thrift footer"
        );
        // Patch FileMetaData.num_rows (first), Id column num_values (second), and RowGroup.num_rows (last)
        // from 2 to 1. Leave list column num_values intact so list repetition levels contain 2 rows
        // while the row group capacity is only 1 row.
        bytes[offsets[0] + 1] = 0x02;
        bytes[offsets[1] + 1] = 0x02;
        bytes[offsets[^1] + 1] = 0x02;

        using var corruptedMs = new MemoryStream(bytes);
        var ex = await Should.ThrowAsync<InvalidDataException>(() =>
            ListRowParquetExtensions.ReadParquetAsync(corruptedMs)
        );
        ex.Message.ShouldContain("produced more rows than row group capacity");
    }

    [Fact]
    public async Task CorruptedDefinitionLevelOutOfBoundsThrowsInvalidDataException()
    {
        var schema = ListRowParquetExtensions.Schema;

        using var ms = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, ms))
        {
            using var groupWriter = writer.CreateRowGroup();

            await groupWriter.WriteAsync<int>(
                (DataField)schema.DataFields[0],
                new ReadOnlyMemory<int>(SingleOneArray)
            );

            // Scores max definition level is 2. Inject corrupted def level = 99
            var scoresField = (DataField)schema.DataFields[2];
            var scoresValues = new ReadOnlyMemory<int>(SingleHundredArray);
            var corruptedDef = new ReadOnlyMemory<int>(SingleNinetyNineArray);
            var repLevels = new ReadOnlyMemory<int>(SingleZeroArray);

            await groupWriter.WriteAllPartsAsync<int>(
                scoresField,
                scoresValues,
                corruptedDef,
                repLevels,
                cancellationToken: default
            );

            await WriteEmptyListColumnsAsync(groupWriter, schema);
        }

        ms.Position = 0;
        var ex = await Should.ThrowAsync<InvalidDataException>(() =>
            ListRowParquetExtensions.ReadParquetAsync(ms)
        );
        ex.Message.ShouldContain("Illegal definition level 99");
    }

    [Fact]
    public async Task CorruptedNegativeDefinitionLevelThrowsInvalidDataException()
    {
        var schema = ListRowParquetExtensions.Schema;

        using var ms = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, ms))
        {
            using var groupWriter = writer.CreateRowGroup();

            await groupWriter.WriteAsync<int>(
                (DataField)schema.DataFields[0],
                new ReadOnlyMemory<int>(SingleOneArray)
            );

            // Scores defLevel injected as -1 (negative definition level)
            var scoresField = (DataField)schema.DataFields[2];
            var scoresValues = new ReadOnlyMemory<int>(SingleHundredArray);
            var corruptedDef = new ReadOnlyMemory<int>(SingleNegativeOneArray);
            var repLevels = new ReadOnlyMemory<int>(SingleZeroArray);

            await groupWriter.WriteAllPartsAsync<int>(
                scoresField,
                scoresValues,
                corruptedDef,
                repLevels,
                cancellationToken: default
            );

            await WriteEmptyListColumnsAsync(groupWriter, schema);
        }

        ms.Position = 0;
        var ex = await Should.ThrowAsync<InvalidDataException>(() =>
            ListRowParquetExtensions.ReadParquetAsync(ms)
        );
        ex.Message.ShouldContain("Illegal definition level");
    }

    [Fact]
    public async Task AbortedHostileReadPreservesArrayPoolHygiene()
    {
        // Verify that repeated exceptions from hostile files do not exhaust or destabilize ArrayPool
        var schema = ListRowParquetExtensions.Schema;

        using var ms = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, ms))
        {
            using var groupWriter = writer.CreateRowGroup();

            await groupWriter.WriteAsync<int>(
                (DataField)schema.DataFields[0],
                new ReadOnlyMemory<int>(SingleOneArray)
            );

            var scoresField = (DataField)schema.DataFields[2];
            var corruptedDef = new ReadOnlyMemory<int>(SingleNinetyNineArray);
            await groupWriter.WriteAllPartsAsync(
                scoresField,
                new ReadOnlyMemory<int>(SingleOneArray),
                corruptedDef,
                new ReadOnlyMemory<int>(SingleZeroArray),
                cancellationToken: default
            );

            await WriteEmptyListColumnsAsync(groupWriter, schema);
        }

        // Run 50 aborted reads in succession — each should fail cleanly with InvalidDataException
        // without throwing OutOfMemoryException or corrupting buffer pools
        for (int i = 0; i < 50; i++)
        {
            ms.Position = 0;
            await Should.ThrowAsync<InvalidDataException>(() =>
                ListRowParquetExtensions.ReadParquetAsync(ms)
            );
        }

        // Verify a normal read succeeds without any pool corruption
        var validItems = new List<ListRow>
        {
            new()
            {
                Id = 100,
                Scores = [1, 2],
                Tags = ["ok"],
                Keys = [Guid.NewGuid()],
            },
        };
        using var validMs = new MemoryStream();
        await validItems.WriteParquetAsync(validMs);
        validMs.Position = 0;
        var restored = await ListRowParquetExtensions.ReadParquetAsync(validMs);
        restored.ShouldHaveSingleItem();
        restored[0].Id.ShouldBe(100);
    }

    [Fact]
    public async Task PhysicalTypeMismatchPositionalThrowsInvalidDataException()
    {
        var schema = new ParquetSchema(new DataField<long>("id"), new DataField<string>("name"));
        using var ms = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, ms)) { }

        ms.Position = 0;
        var ex = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetAsync(ms)
        );
        ex.Message.ShouldContain(
            "Column 'id' type 'System.Int64' does not match expected 'System.Int32'"
        );
    }

    [Fact]
    public async Task PhysicalTypeMismatchReorderedThrowsInvalidDataException()
    {
        var schema = new ParquetSchema(new DataField<string>("name"), new DataField<string>("id"));
        using var ms = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, ms)) { }

        ms.Position = 0;
        var ex = await Should.ThrowAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetAsync(ms)
        );
        ex.Message.ShouldContain("Column 'id' type");
        ex.Message.ShouldContain("does not match expected 'System.Int32'");
    }

    [Fact]
    public async Task PhysicalMetadataMismatchIsRejectedAcrossGeneratedReadPaths()
    {
        var items = new List<MultiRowGroupModel>
        {
            new() { Id = 1, Name = "one" },
        };

        using var source = new MemoryStream();
        await items.WriteParquetAsync(source);
        byte[] hostileBytes = await RewriteFirstColumnPhysicalTypeAsync(
            source.ToArray(),
            ParquetPhysicalType.BYTE_ARRAY
        );

        using (var listStream = new MemoryStream(hostileBytes, writable: false))
        {
            var ex = await Should.ThrowAsync<InvalidDataException>(() =>
                MultiRowGroupModelParquetExtensions.ReadParquetAsync(listStream)
            );
            ex.Message.ShouldContain("physical type 'BYTE_ARRAY'");
            ex.Message.ShouldContain("expected 'INT32'");
        }

        using (var arrayStream = new MemoryStream(hostileBytes, writable: false))
        {
            var ex = await Should.ThrowAsync<InvalidDataException>(() =>
                MultiRowGroupModelParquetExtensions.ReadParquetArrayAsync(arrayStream)
            );
            ex.Message.ShouldContain("physical type 'BYTE_ARRAY'");
        }

        using (var parallelStream = new MemoryStream(hostileBytes, writable: false))
        {
            var ex = await Should.ThrowAsync<InvalidDataException>(() =>
                MultiRowGroupModelParquetExtensions.ReadParquetParallelArrayAsync(parallelStream)
            );
            ex.Message.ShouldContain("physical type 'BYTE_ARRAY'");
        }

        using (var stream = new MemoryStream(hostileBytes, writable: false))
        {
            var ex = await Should.ThrowAsync<InvalidDataException>(async () =>
            {
                await foreach (
                    var _ in MultiRowGroupModelParquetExtensions.ReadParquetStreamAsync(stream)
                ) { }
            });
            ex.Message.ShouldContain("physical type 'BYTE_ARRAY'");
        }
    }

    [Theory]
    [InlineData("before-header", "outside the file data bounds")]
    [InlineData("in-footer", "outside the file data bounds")]
    [InlineData("past-footer", "extends beyond the file data bounds")]
    [InlineData("overlap", "ranges overlap")]
    public async Task MalformedColumnChunkBoundsAreRejected(string mutation, string expectedMessage)
    {
        var items = new List<MultiRowGroupModel>
        {
            new() { Id = 1, Name = "one" },
        };

        using var source = new MemoryStream();
        await items.WriteParquetAsync(source);
        byte[] hostileBytes = await RewriteColumnMetadataAsync(
            source.ToArray(),
            (metadata, footerStart) =>
            {
                var first = metadata.RowGroups[0].Columns[0].MetaData!;
                switch (mutation)
                {
                    case "before-header":
                        first.DataPageOffset = -1;
                        break;
                    case "in-footer":
                        first.DataPageOffset = footerStart;
                        break;
                    case "past-footer":
                        first.TotalCompressedSize = long.MaxValue;
                        break;
                    case "overlap":
                        metadata.RowGroups[0].Columns[1].MetaData!.DataPageOffset =
                            first.DataPageOffset;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
                }
            }
        );

        using var hostileStream = new MemoryStream(hostileBytes, writable: false);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            MultiRowGroupModelParquetExtensions.ReadParquetAsync(hostileStream)
        );
        Assert.Contains(expectedMessage, ex.Message);
    }

    private static async Task<byte[]> RewriteFirstColumnPhysicalTypeAsync(
        byte[] bytes,
        ParquetPhysicalType physicalType
    )
    {
        return await RewriteColumnMetadataAsync(
            bytes,
            (metadata, _) => metadata.RowGroups[0].Columns[0].MetaData!.Type = physicalType
        );
    }

    private static async Task<byte[]> RewriteColumnMetadataAsync(
        byte[] bytes,
        Action<FileMetaData, int> mutate
    )
    {
        int footerLength = BitConverter.ToInt32(bytes, bytes.Length - 8);
        int footerStart = bytes.Length - 8 - footerLength;

        using var input = new MemoryStream(bytes, writable: false);
        await using var reader = await ParquetReader.CreateAsync(input);
        mutate(reader.Metadata!, footerStart);

        using var footer = new MemoryStream();
        System.Type writerType = typeof(ParquetReader).Assembly.GetType(
            "Parquet.Meta.Proto.ThriftCompactProtocolWriter",
            throwOnError: true
        )!;
        object writer = Activator.CreateInstance(
            writerType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [footer],
            culture: CultureInfo.InvariantCulture
        )!;
        MethodInfo writeMethod = typeof(FileMetaData).GetMethod(
            "Write",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        )!;
        writeMethod.Invoke(reader.Metadata, [writer]);

        byte[] result = new byte[footerStart + footer.Length + 8];
        Buffer.BlockCopy(bytes, 0, result, 0, footerStart);
        footer.ToArray().AsSpan().CopyTo(result.AsSpan(footerStart));
        BitConverter.GetBytes((int)footer.Length).CopyTo(result, footerStart + (int)footer.Length);
        "PAR1"u8.CopyTo(result.AsSpan(result.Length - 4));
        return result;
    }

    private static async Task WriteEmptyListColumnsAsync(
        ParquetRowGroupWriter groupWriter,
        ParquetSchema schema
    )
    {
        await groupWriter.WriteAllPartsAsync<ReadOnlyMemory<char>>(
            (DataField)schema.DataFields[1],
            ReadOnlyMemory<ReadOnlyMemory<char>>.Empty,
            new ReadOnlyMemory<int>(SingleZeroArray),
            new ReadOnlyMemory<int>(SingleZeroArray),
            cancellationToken: default
        );
        await groupWriter.WriteAllPartsAsync<Guid>(
            (DataField)schema.DataFields[3],
            ReadOnlyMemory<Guid>.Empty,
            new ReadOnlyMemory<int>(SingleZeroArray),
            new ReadOnlyMemory<int>(SingleZeroArray),
            cancellationToken: default
        );
        await groupWriter.WriteAllPartsAsync<DateTime>(
            (DataField)schema.DataFields[4],
            ReadOnlyMemory<DateTime>.Empty,
            new ReadOnlyMemory<int>(SingleZeroArray),
            new ReadOnlyMemory<int>(SingleZeroArray),
            cancellationToken: default
        );
        await groupWriter.WriteAllPartsAsync<ReadOnlyMemory<byte>>(
            (DataField)schema.DataFields[5],
            ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty,
            new ReadOnlyMemory<int>(SingleZeroArray),
            new ReadOnlyMemory<int>(SingleZeroArray),
            cancellationToken: default
        );
    }
}
