using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using IOFile = System.IO.File;

namespace Parquet.SourceGenerator.Benchmarks;

[ParquetSerializable]
public partial record PrefetchEvent
{
    [ParquetColumn("Id")]
    public int Id { get; init; }

    [ParquetColumn("ValA")]
    public double ValA { get; init; }

    [ParquetColumn("ValB")]
    public long ValB { get; init; }

    [ParquetColumn("Label")]
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// A read-only stream that charges a fixed cost per read call, standing in for a network or
/// cold-disk source. Prefetching can only pay for itself where there is latency to hide, so the
/// warm-buffer numbers alone would not answer the question #146 actually asks.
/// </summary>
internal sealed class LatencyStream : Stream
{
    private readonly MemoryStream _inner;
    private readonly int _microsecondsPerRead;

    public LatencyStream(byte[] bytes, int microsecondsPerRead)
    {
        _inner = new MemoryStream(bytes, writable: false);
        _microsecondsPerRead = microsecondsPerRead;
    }

    /// <summary>Read calls served, so the modelled latency can be reported per pass, not per call.</summary>
    public int ReadCount;

    private void Stall()
    {
        if (_microsecondsPerRead <= 0)
        {
            return;
        }

        // A busy spin, not a sleep. Thread.Sleep's resolution is orders of magnitude coarser than
        // the latency being modelled, and SpinWait escalates to a sleep of its own after a few
        // iterations. Burning the core is also the honest model: it keeps the read on the critical
        // path exactly as a real syscall would, which is the thing prefetching claims to hide.
        long ticks = (long)(
            _microsecondsPerRead * (System.Diagnostics.Stopwatch.Frequency / 1_000_000.0)
        );
        long until = System.Diagnostics.Stopwatch.GetTimestamp() + ticks;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until)
        {
            Thread.SpinWait(16);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ReadCount++;
        Stall();
        return _inner.Read(buffer, offset, count);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// #146: does overlapping row-group decode with the caller's own work reduce wall clock?
/// Every method consumes the same file and does the same per-item work; only the source stream and
/// the <c>PrefetchNextRowGroup</c> flag vary.
/// </summary>
[MemoryDiagnoser]
[InProcess]
[WarmupCount(2)]
[IterationCount(6)]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.Declared)]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class PrefetchStreamingBenchmark
{
    private byte[] _parquetBytes = null!;
    private string _tempFile = null!;

    private static readonly ParquetSerializerOptions Sequential = new()
    {
        PrefetchNextRowGroup = false,
    };

    private static readonly ParquetSerializerOptions Prefetched = new()
    {
        PrefetchNextRowGroup = true,
        PrefetchDepth = 1,
    };

    /// <summary>Rows in the file.</summary>
    [Params(500_000)]
    public int Count { get; set; }

    /// <summary>
    /// Simulated per-item consumer cost, in spin iterations. Zero is the "tight loop" case where
    /// there is nothing for the decode to hide behind.
    /// </summary>
    [Params(0, 50)]
    public int ConsumerWorkPerItem { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var data = Enumerable
            .Range(0, Count)
            .Select(i => new PrefetchEvent
            {
                Id = i,
                ValA = i * 3.14159,
                ValB = i * 1000L,
                Label = $"label_{i % 512}",
            })
            .ToList();

        using var stream = new MemoryStream();
        data.WriteParquetBatchedAsync(stream, rowGroupSize: 20_000).GetAwaiter().GetResult();
        _parquetBytes = stream.ToArray();

        _tempFile = Path.Combine(Path.GetTempPath(), $"prefetch-bench-{Guid.NewGuid():N}.parquet");
        IOFile.WriteAllBytes(_tempFile, _parquetBytes);

        // Report the read count so the modelled latency can be read as a total, not a per-call
        // number nobody can size: reads x MicrosecondsPerRead is the latency the prefetch has to hide.
        using var probe = new LatencyStream(_parquetBytes, microsecondsPerRead: 0);
        DrainAsync(probe, Sequential).GetAwaiter().GetResult();
        Console.WriteLine(
            $"// LatencyStream: {probe.ReadCount} reads per pass; at {MicrosecondsPerRead}us/read that is "
                + $"{probe.ReadCount * MicrosecondsPerRead / 1000.0:F1} ms of modelled I/O latency per op."
        );
    }

    /// <summary>Modelled per-read latency for the slow-stream scenario.</summary>
    private const int MicrosecondsPerRead = 200;

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_tempFile != null && IOFile.Exists(_tempFile))
        {
            IOFile.Delete(_tempFile);
        }
    }

    private double Consume(PrefetchEvent item)
    {
        double acc = item.ValA + item.ValB;
        for (int w = 0; w < ConsumerWorkPerItem; w++)
        {
            acc = acc * 1.0000001 + 1.0;
        }

        return acc;
    }

    private async Task<double> DrainAsync(Stream stream, ParquetSerializerOptions options)
    {
        double acc = 0;
        await foreach (
            var item in PrefetchEventParquetExtensions.ReadParquetStreamAsync(stream, options)
        )
        {
            acc += Consume(item);
        }

        return acc;
    }

    // ── in-memory buffer: no I/O latency at all ─────────────────────────────────

    [Benchmark(Baseline = true)]
    public async Task<double> Memory_Sequential()
    {
        using var stream = new MemoryStream(_parquetBytes);
        return await DrainAsync(stream, Sequential);
    }

    [Benchmark]
    public async Task<double> Memory_Prefetched()
    {
        using var stream = new MemoryStream(_parquetBytes);
        return await DrainAsync(stream, Prefetched);
    }

    // ── local file, warm page cache ─────────────────────────────────────────────

    [Benchmark]
    public async Task<double> WarmFile_Sequential()
    {
        using var stream = new FileStream(_tempFile, FileMode.Open, FileAccess.Read);
        return await DrainAsync(stream, Sequential);
    }

    [Benchmark]
    public async Task<double> WarmFile_Prefetched()
    {
        using var stream = new FileStream(_tempFile, FileMode.Open, FileAccess.Read);
        return await DrainAsync(stream, Prefetched);
    }

    // ── synthetic latency, standing in for network/cold storage ─────────────────

    [Benchmark]
    public async Task<double> SlowStream_Sequential()
    {
        using var stream = new LatencyStream(
            _parquetBytes,
            microsecondsPerRead: MicrosecondsPerRead
        );
        return await DrainAsync(stream, Sequential);
    }

    [Benchmark]
    public async Task<double> SlowStream_Prefetched()
    {
        using var stream = new LatencyStream(
            _parquetBytes,
            microsecondsPerRead: MicrosecondsPerRead
        );
        return await DrainAsync(stream, Prefetched);
    }
}
