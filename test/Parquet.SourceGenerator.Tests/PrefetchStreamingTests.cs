using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record PrefetchModel
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("name")]
    public string Name { get; init; } = string.Empty;

    [ParquetColumn("category")]
    public string? Category { get; init; }

    [ParquetColumn("score")]
    public double? Score { get; init; }
}

/// <summary>
/// A seekable stream that hands out the bytes of a backing buffer, counts what has been read, and
/// can be told to fail once a byte threshold is crossed. Enough to prove the prefetch producer both
/// stops when the consumer walks away and surfaces its failures to the caller.
/// </summary>
internal sealed class InstrumentedStream : Stream
{
    private readonly MemoryStream _inner;
    private long _bytesRead;
    private long _failAfterBytes = long.MaxValue;

    public InstrumentedStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);

    /// <summary>Total bytes handed to the reader so far, across all threads.</summary>
    public long BytesRead => Interlocked.Read(ref _bytesRead);

    /// <summary>Number of read calls, whatever their size.</summary>
    public int ReadCount;

    /// <summary>Once this many bytes have been read, every subsequent read throws.</summary>
    public long FailAfterBytes
    {
        get => Interlocked.Read(ref _failAfterBytes);
        set => Interlocked.Exchange(ref _failAfterBytes, value);
    }

    /// <summary>Marker exception, so a test can prove the caller sees this exact failure.</summary>
    internal sealed class DecodeBoom : IOException
    {
        public DecodeBoom()
            : base("prefetch decode boom") { }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Interlocked.Increment(ref ReadCount);
        if (Interlocked.Read(ref _bytesRead) >= FailAfterBytes)
        {
            throw new DecodeBoom();
        }

        int read = _inner.Read(buffer, offset, count);
        Interlocked.Add(ref _bytesRead, read);
        return read;
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

public sealed class PrefetchStreamingTests
{
    private const int RowCount = 2_000;
    private const int RowGroupSize = 100;

    private static List<PrefetchModel> BuildItems(int count) =>
        Enumerable
            .Range(0, count)
            .Select(i => new PrefetchModel
            {
                Id = i,
                Name = $"name_{i}",
                Category = i % 4 == 0 ? null : $"cat_{i % 7}",
                Score = i % 5 == 0 ? null : i * 1.5,
            })
            .ToList();

    private static async Task<byte[]> WriteAsync(int count = RowCount)
    {
        using var stream = new MemoryStream();
        await BuildItems(count).WriteParquetBatchedAsync(stream, rowGroupSize: RowGroupSize);
        return stream.ToArray();
    }

    private static ParquetSerializerOptions Prefetching(int depth = 1) =>
        new() { PrefetchNextRowGroup = true, PrefetchDepth = depth };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(0)] // clamped up to 1 rather than rejected or deadlocking on a zero-capacity channel
    [InlineData(-5)]
    public async Task PrefetchedStreamYieldsExactlyTheSequentialSequence(int depth)
    {
        byte[] bytes = await WriteAsync();

        var sequential = new List<PrefetchModel>();
        using (var stream = new MemoryStream(bytes))
        {
            await foreach (
                var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(stream)
            )
            {
                sequential.Add(item);
            }
        }

        var prefetched = new List<PrefetchModel>();
        using (var stream = new MemoryStream(bytes))
        {
            await foreach (
                var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                    stream,
                    Prefetching(depth)
                )
            )
            {
                prefetched.Add(item);
            }
        }

        Assert.Equal(RowCount, sequential.Count);
        Assert.Equal(sequential, prefetched);
    }

    [Fact]
    public async Task PrefetchedStreamDeduplicatesStringsExactlyAsTheSequentialReaderDoes()
    {
        byte[] bytes = await WriteAsync();

        // The deduplicator is a fixed-size direct-mapped cache, so which instances end up shared is
        // a property of the read order, not a guarantee per value. The claim worth testing is that
        // moving materialisation onto the prefetch task does not change that outcome at all.
        int sequentialDistinct = await CountDistinctCategoryInstancesAsync(bytes, prefetch: false);
        int prefetchedDistinct = await CountDistinctCategoryInstancesAsync(bytes, prefetch: true);

        Assert.Equal(sequentialDistinct, prefetchedDistinct);

        // And it must actually be deduplicating: one instance per row would be 1,500 of them.
        Assert.True(
            prefetchedDistinct < RowCount / 4,
            $"expected the categories to collapse, saw {prefetchedDistinct} distinct instances"
        );
    }

    private static async Task<int> CountDistinctCategoryInstancesAsync(byte[] bytes, bool prefetch)
    {
        var options = new ParquetSerializerOptions
        {
            DeduplicateStrings = true,
            PrefetchNextRowGroup = prefetch,
        };

        var instances = new HashSet<string>(
            (IEqualityComparer<string>)ReferenceEqualityComparer.Instance
        );

        using var stream = new MemoryStream(bytes);
        await foreach (
            var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(stream, options)
        )
        {
            if (item.Category is not null)
            {
                instances.Add(item.Category);
            }
        }

        return instances.Count;
    }

    [Fact]
    public async Task PrefetchedStreamIsOffByDefault()
    {
        var options = ParquetSerializerOptions.Default;
        Assert.False(options.PrefetchNextRowGroup);
        Assert.Equal(1, options.PrefetchDepth);

        byte[] bytes = await WriteAsync(count: 10);
        using var stream = new MemoryStream(bytes);
        int rows = 0;
        await foreach (
            var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(stream, options)
        )
        {
            rows++;
        }

        Assert.Equal(10, rows);
    }

    [Fact]
    public async Task PrefetchedStreamSurfacesTheProducerExceptionUnwrapped()
    {
        byte[] bytes = await WriteAsync();

        using var stream = new InstrumentedStream(bytes);

        // Fail immediately: the failure lands inside ParquetReader.CreateAsync, before a single
        // row group is published, so the only route out is the awaited producer task.
        stream.FailAfterBytes = 0;

        var thrown = await Assert.ThrowsAsync<InstrumentedStream.DecodeBoom>(async () =>
        {
            await foreach (
                var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                    stream,
                    Prefetching()
                )
            )
            {
                Assert.NotNull(item);
            }
        });

        // Not a ChannelClosedException, not an AggregateException: the caller's catch block should
        // be able to name the exception the decode actually threw.
        Assert.Equal("prefetch decode boom", thrown.Message);
    }

    [Fact]
    public async Task PrefetchedStreamSurfacesAProducerExceptionRaisedAfterItemsWereYielded()
    {
        byte[] bytes = await WriteAsync();

        using var stream = new InstrumentedStream(bytes);
        var seen = new List<PrefetchModel>();

        await Assert.ThrowsAsync<InstrumentedStream.DecodeBoom>(async () =>
        {
            await foreach (
                var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                    stream,
                    Prefetching()
                )
            )
            {
                seen.Add(item);

                // Arm the failure once the first row group is in the caller's hands, so the
                // producer trips on a later row group while the consumer is mid-enumeration.
                if (seen.Count == 1)
                {
                    stream.FailAfterBytes = stream.BytesRead;
                }
            }
        });

        Assert.NotEmpty(seen);
        Assert.True(
            seen.Count < RowCount,
            $"expected the read to abort early, but it returned all {seen.Count} rows"
        );
    }

    [Fact]
    public async Task PrefetchedStreamStopsTheProducerWhenTheConsumerBreaksEarly()
    {
        byte[] bytes = await WriteAsync();

        using var stream = new InstrumentedStream(bytes);

        await foreach (
            var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                stream,
                Prefetching(depth: 2)
            )
        )
        {
            Assert.Equal(0, item.Id);
            break;
        }

        // The await foreach has disposed the enumerator, which must have torn the producer down
        // synchronously with respect to this thread. If it had not, the reader would still be
        // running ahead through the remaining 19 row groups.
        long readsAtBreak = stream.BytesRead;
        await Task.Delay(150);
        Assert.Equal(readsAtBreak, stream.BytesRead);
        Assert.True(
            readsAtBreak < bytes.Length,
            "breaking after the first row group should not have read the whole file"
        );
    }

    [Fact]
    public async Task PrefetchedStreamDoesNotLeaveConsumedRowGroupsRootedInThePool()
    {
        byte[] bytes = await WriteAsync();

        // If a pooled item array were returned to ArrayPool without being cleared, every object of
        // the row groups already consumed would stay reachable from the pool for the lifetime of
        // the process. Weak references are the only way to observe that from outside.
        List<WeakReference> witnesses = await CollectWitnessesAsync(bytes);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }

        int alive = witnesses.Count(w => w.IsAlive);
        Assert.True(
            alive == 0,
            $"{alive} of {witnesses.Count} consumed rows are still reachable — a pooled array is rooting them"
        );
    }

    // Kept out of the test body so no stack slot of the caller can keep the items alive.
    private static async Task<List<WeakReference>> CollectWitnessesAsync(byte[] bytes)
    {
        var witnesses = new List<WeakReference>();
        using var stream = new MemoryStream(bytes);

        int seen = 0;
        await foreach (
            var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                stream,
                new ParquetSerializerOptions { PrefetchNextRowGroup = true, PrefetchDepth = 2 }
            )
        )
        {
            // One witness per row group boundary, spread across the whole file.
            if (seen % RowGroupSize == 0)
            {
                witnesses.Add(new WeakReference(item));
            }

            seen++;

            // Break part-way so the teardown drain, not just the steady-state loop, is exercised.
            if (seen == RowCount / 2)
            {
                break;
            }
        }

        return witnesses;
    }

    [Fact]
    public async Task PrefetchedStreamHonoursCancellationMidEnumeration()
    {
        byte[] bytes = await WriteAsync();

        using var cts = new CancellationTokenSource();
        using var stream = new InstrumentedStream(bytes);
        int seen = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (
                var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                    stream,
                    Prefetching(depth: 2),
                    cts.Token
                )
            )
            {
                seen++;
                if (seen == RowGroupSize + 1)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(seen >= RowGroupSize);

        long readsAtCancel = stream.BytesRead;
        await Task.Delay(150);
        Assert.Equal(readsAtCancel, stream.BytesRead);
    }

    [Fact]
    public async Task PrefetchedStreamHonoursAnAlreadyCancelledToken()
    {
        byte[] bytes = await WriteAsync(count: 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (
                var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                    stream,
                    Prefetching(),
                    cts.Token
                )
            )
            {
                Assert.NotNull(item);
            }
        });
    }

    [Fact]
    public async Task PrefetchedStreamRejectsANullStream()
    {
        await Task.Yield();
        Assert.Throws<ArgumentNullException>(() =>
            PrefetchModelParquetExtensions.ReadParquetStreamAsync((Stream)null!, Prefetching())
        );
    }

    [Fact]
    public async Task PrefetchedBufferOverloadStreamsTheSameSequence()
    {
        byte[] bytes = await WriteAsync();

        var prefetched = new List<PrefetchModel>();
        await foreach (
            var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(
                new ReadOnlyMemory<byte>(bytes),
                Prefetching()
            )
        )
        {
            prefetched.Add(item);
        }

        Assert.Equal(BuildItems(RowCount), prefetched);
    }

    [Fact]
    public async Task PrefetchedStreamHandlesAnEmptyFile()
    {
        byte[] bytes = await WriteAsync(count: 0);

        using var stream = new MemoryStream(bytes);
        await foreach (
            var item in PrefetchModelParquetExtensions.ReadParquetStreamAsync(stream, Prefetching())
        )
        {
            Assert.Fail($"expected no rows, got {item.Id}");
        }
    }
}
