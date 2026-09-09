using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Models;
using Xunit;
using IOFile = System.IO.File;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record MappedRow
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string Name { get; init; } = string.Empty;

    [ParquetColumn("score")]
    public double? Score { get; init; }
}

/// <summary>
/// Reading a file used to mean opening a <c>FileStream</c> by hand, which copies every byte through
/// the stream's buffer and gives parallel workers nothing to share — a stream cannot be read from
/// two cursors at once. The <c>FileInfo</c> overloads map the file instead, so the reader seeks
/// within kernel pages and every worker gets its own cursor over them.
/// </summary>
public sealed class MemoryMappedFileReadTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "psg-mmf-" + Guid.NewGuid().ToString("N")
    );

    public MemoryMappedFileReadTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private async Task<FileInfo> WriteSampleAsync(int rowCount, int rowGroupSize)
    {
        List<MappedRow> rows = Enumerable
            .Range(1, rowCount)
            .Select(i => new MappedRow
            {
                Id = i,
                Name = $"Item_{i}",
                Score = i % 3 == 0 ? null : i * 1.5,
            })
            .ToList();

        string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".parquet");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            await rows.WriteParquetBatchedAsync(stream, rowGroupSize);
        }

        return new FileInfo(path);
    }

    [Fact]
    public async Task SequentialFileReadReturnsEveryRow()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 500, rowGroupSize: 50);

        List<MappedRow> read = await MappedRowParquetExtensions.ReadParquetAsync(file);

        Assert.Equal(500, read.Count);
        Assert.Equal(Enumerable.Range(1, 500), read.Select(r => r.Id));
        Assert.Equal("Item_7", read[6].Name);
        Assert.Null(read[2].Score);
    }

    [Fact]
    public async Task ArrayFileReadReturnsEveryRow()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 300, rowGroupSize: 40);

        MappedRow[] read = await MappedRowParquetExtensions.ReadParquetArrayAsync(file);

        Assert.Equal(300, read.Length);
        Assert.Equal(Enumerable.Range(1, 300), read.Select(r => r.Id));
    }

    [Fact]
    public async Task ParallelFileReadPreservesRowOrderAcrossWorkers()
    {
        // Enough rows to clear the sequential fast path inside the parallel reader.
        FileInfo file = await WriteSampleAsync(rowCount: 40_000, rowGroupSize: 2_000);

        List<MappedRow> read = await MappedRowParquetExtensions.ReadParquetParallelAsync(
            file,
            maxDegreeOfParallelism: 4
        );

        Assert.Equal(40_000, read.Count);
        Assert.Equal(Enumerable.Range(1, 40_000), read.Select(r => r.Id));
    }

    [Fact]
    public async Task ParallelArrayFileReadMatchesTheStreamReader()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 40_000, rowGroupSize: 2_000);

        MappedRow[] mapped = await MappedRowParquetExtensions.ReadParquetParallelArrayAsync(file);

        using var stream = file.OpenRead();
        List<MappedRow> viaStream = await MappedRowParquetExtensions.ReadParquetAsync(stream);

        Assert.Equal(viaStream, mapped);
    }

    [Fact]
    public async Task StreamingFileReadYieldsRowsInOrder()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 250, rowGroupSize: 25);

        var ids = new List<int>();
        await foreach (MappedRow row in MappedRowParquetExtensions.ReadParquetStreamAsync(file))
        {
            ids.Add(row.Id);
        }

        Assert.Equal(Enumerable.Range(1, 250), ids);
    }

    /// <summary>
    /// The mapping is an implementation detail: switching it off must not change a single row.
    /// </summary>
    [Fact]
    public async Task DisablingMappingTakesTheStreamPathAndReadsIdenticalRows()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 40_000, rowGroupSize: 2_000);
        var noMapping = new ParquetSerializerOptions { UseMemoryMappedFiles = false };

        MappedRow[] viaStream = await MappedRowParquetExtensions.ReadParquetParallelArrayAsync(
            file,
            options: noMapping
        );
        MappedRow[] viaMapping = await MappedRowParquetExtensions.ReadParquetParallelArrayAsync(
            file
        );

        Assert.Equal(viaMapping, viaStream);
    }

    /// <summary>
    /// A zero-length file cannot be mapped at all — <c>CreateFromFile</c> rejects it — so the
    /// overloads must fall back to the stream path and fail the way a stream read fails, rather
    /// than surfacing a memory-mapping error.
    /// </summary>
    [Fact]
    public async Task EmptyFileFallsBackToTheStreamPath()
    {
        string path = Path.Combine(_directory, "empty.parquet");
        IOFile.WriteAllBytes(path, Array.Empty<byte>());
        var file = new FileInfo(path);

        Assert.Null(ParquetMemoryMappedFile.TryOpen(file));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            MappedRowParquetExtensions.ReadParquetAsync(file)
        );
    }

    [Fact]
    public async Task NullFileIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            MappedRowParquetExtensions.ReadParquetAsync((FileInfo)null!)
        );
    }

    [Fact]
    public async Task CancellationIsObserved()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 200, rowGroupSize: 25);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MappedRowParquetExtensions.ReadParquetParallelArrayAsync(
                file,
                cancellationToken: cts.Token
            )
        );
    }

    /// <summary>
    /// The mapping owns unmanaged pages, so a read that returns without releasing them leaks the
    /// view for the lifetime of the process. Deleting the file afterwards is the observable proxy:
    /// on Windows an undisposed mapping keeps a handle and the delete fails.
    /// </summary>
    [Fact]
    public async Task MappingIsReleasedSoTheFileCanBeDeletedAfterwards()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 100, rowGroupSize: 20);

        _ = await MappedRowParquetExtensions.ReadParquetAsync(file);
        _ = await MappedRowParquetExtensions.ReadParquetParallelArrayAsync(file);
        await foreach (MappedRow _ in MappedRowParquetExtensions.ReadParquetStreamAsync(file)) { }

        IOFile.Delete(file.FullName);
        Assert.False(IOFile.Exists(file.FullName));
    }

    [Fact]
    public async Task MappedMemoryIsRejectedAfterDisposal()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 10, rowGroupSize: 5);

        ParquetMemoryMappedFile? mapped = ParquetMemoryMappedFile.TryOpen(file);
        Assert.NotNull(mapped);
        Assert.Equal(file.Length, mapped!.Length);
        Assert.Equal(file.Length, mapped.Memory.Length);

        mapped.Dispose();
        // Double disposal must be harmless: releasing the view pointer twice would corrupt the handle.
        mapped.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mapped.Memory);
    }

    /// <summary>
    /// The mapped bytes must be the file's bytes, not a view of some other offset.
    /// </summary>
    [Fact]
    public async Task MappedMemoryMatchesTheFileContents()
    {
        FileInfo file = await WriteSampleAsync(rowCount: 50, rowGroupSize: 10);

        using ParquetMemoryMappedFile? mapped = ParquetMemoryMappedFile.TryOpen(file);

        Assert.NotNull(mapped);
        Assert.Equal(IOFile.ReadAllBytes(file.FullName), mapped!.Memory.ToArray());
    }

    /// <summary>
    /// Pins the file overloads in the emitted source, so a future refactor cannot quietly drop them
    /// or re-introduce a whole-file copy on the way to the reader.
    /// </summary>
    [Fact]
    public void EmittedSourceCarriesTheFileOverloads()
    {
        var model = new TargetClassModel(
            Namespace: "TestNamespace",
            ClassName: "TestEntity",
            Properties: new EquatableArray<PropertyModel>(
                new[]
                {
                    new PropertyModel(
                        "Id",
                        "id",
                        "int",
                        null,
                        null,
                        1,
                        null,
                        null,
                        PropertyKind.Primitive,
                        false
                    ),
                }
            )
        );

        string source = CodeEmitter.EmitSource(model);

        Assert.Contains("ParquetMemoryMappedFile.TryOpen(file)", source, StringComparison.Ordinal);
        Assert.Contains(
            "private static global::System.IO.FileStream OpenReadFileStream(",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains("global::System.IO.FileInfo file,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.ReadAllBytes", source, StringComparison.Ordinal);
    }
}
