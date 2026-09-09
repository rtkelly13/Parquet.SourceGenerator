using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using IOFile = System.IO.File;

namespace Parquet.SourceGenerator.Benchmarks;

[ParquetSerializable]
public partial record FileRow
{
    [ParquetColumn("Id")]
    public int Id { get; init; }

    [ParquetColumn("ValA")]
    public double ValA { get; init; }

    [ParquetColumn("ValB")]
    public long ValB { get; init; }

    [ParquetColumn("IsValid")]
    public bool IsValid { get; init; }
}

/// <summary>
/// Compares reading a Parquet file from disk through a buffered <c>FileStream</c> against mapping it
/// into memory, for both the sequential and the parallel reader.
/// </summary>
/// <remarks>
/// <para>
/// The question the mapping is meant to answer is whether removing the <c>FileStream</c> buffer copy
/// (and, for the parallel reader, the whole-file <c>byte[]</c> that sharing a buffer across workers
/// otherwise requires) is worth anything once decoding costs are counted.
/// </para>
/// <para>
/// Every iteration reads the same file, so the OS page cache is warm throughout: this measures the
/// user-space cost of getting bytes to the decompressor, not disk latency. A cold-cache run would
/// favour whichever path the kernel reads ahead for, which is the stream.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[InProcess]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class FileReadBenchmark
{
    private static readonly string Directory = Path.Combine(Path.GetTempPath(), "psg-mmf-bench");

    private FileInfo _file = null!;

    /// <summary>
    /// Rows written to the file under test. 4,000,000 rows of four scalar columns is about 64 MB of
    /// Snappy-compressed Parquet, which is enough for per-byte costs to dominate per-call ones.
    /// </summary>
    [Params(4_000_000)]
    public int Count { get; set; }

    /// <summary>
    /// Writes the file under test once and reuses it for every benchmark in the run.
    /// </summary>
    /// <remarks>
    /// Written to a fixed path rather than a per-instance temp directory: BenchmarkDotNet builds a
    /// fresh instance per benchmark case, and regenerating four million rows for each of them costs
    /// more than the whole measurement. The file is left behind deliberately — a later run reuses it.
    /// </remarks>
    [GlobalSetup]
    public void Setup()
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = Path.Combine(Directory, $"rows-{Count}.parquet");

        if (!IOFile.Exists(path))
        {
            List<FileRow> data = Enumerable
                .Range(0, Count)
                .Select(i => new FileRow
                {
                    Id = i,
                    ValA = i * 3.14159,
                    ValB = i * 1000L,
                    IsValid = i % 2 == 0,
                })
                .ToList();

            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            {
                // Blocking rather than an async [GlobalSetup]: BenchmarkDotNet's in-process
                // toolchain skips an async setup here, which leaves every benchmark reading a
                // null file.
                data.WriteParquetBatchedAsync(stream, rowGroupSize: 100_000)
                    .GetAwaiter()
                    .GetResult();
            }

            IOFile.Move(temporary, path);
        }

        _file = new FileInfo(path);
        Console.WriteLine($"[FileReadBenchmark] {_file.Length:N0} bytes on disk");
    }

    /// <summary>
    /// The status quo for sequential file reads: open a buffered stream and hand it to the reader.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<int> SequentialViaFileStream()
    {
        using var stream = new FileStream(
            _file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        FileRow[] rows = await FileRowParquetExtensions.ReadParquetArrayAsync(stream);
        return rows.Length;
    }

    /// <summary>
    /// The same read over mapped pages.
    /// </summary>
    [Benchmark]
    public async Task<int> SequentialViaMemoryMappedFile()
    {
        FileRow[] rows = await FileRowParquetExtensions.ReadParquetArrayAsync(_file);
        return rows.Length;
    }

    /// <summary>
    /// The parallel reader over a stream, which cannot actually parallelise: a stream has one cursor,
    /// so this is the sequential reader wearing a different name.
    /// </summary>
    [Benchmark]
    public async Task<int> ParallelViaFileStream()
    {
        using var stream = new FileStream(
            _file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        FileRow[] rows = await FileRowParquetExtensions.ReadParquetParallelArrayAsync(stream);
        return rows.Length;
    }

    /// <summary>
    /// The way a caller had to reach the parallel buffer reader before the file overloads existed:
    /// pull the whole file onto the managed heap first.
    /// </summary>
    [Benchmark]
    public async Task<int> ParallelViaReadAllBytes()
    {
        byte[] bytes = IOFile.ReadAllBytes(_file.FullName);
        FileRow[] rows = await FileRowParquetExtensions.ReadParquetParallelArrayAsync(bytes);
        return rows.Length;
    }

    /// <summary>
    /// The parallel reader over mapped pages: no whole-file copy, and every worker gets its own cursor.
    /// </summary>
    [Benchmark]
    public async Task<int> ParallelViaMemoryMappedFile()
    {
        FileRow[] rows = await FileRowParquetExtensions.ReadParquetParallelArrayAsync(_file);
        return rows.Length;
    }
}
