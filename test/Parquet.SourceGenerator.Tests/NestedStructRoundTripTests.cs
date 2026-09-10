using System.IO;
using Parquet.SourceGenerator;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// M2 of issue #176: nested POCO members (StructField groups) round-trip through the v6
/// generated code — optional and required members, null propagation at each level, and
/// struct-in-struct composition. The generator runs as an analyzer on this assembly, so
/// these exercise the real emitted extraction/reconstruction paths.
/// </summary>
public sealed class NestedStructRoundTripTests
{
    [Fact]
    public async Task OptionalStructMemberRoundTripsWithNullPropagation()
    {
        var rows = new List<NestedOrder>
        {
            new()
            {
                Id = 0,
                Ship = new Address { City = "NYC", Zip = 10001 },
                Bill = new Address { City = "Boston", Zip = 2000 },
            },
            new()
            {
                Id = 1,
                Ship = null,
                Bill = new Address(),
            },
            new()
            {
                Id = 2,
                Ship = new Address { City = null, Zip = 55 },
                Bill = new Address { City = "LA", Zip = null },
            },
        };

        var ms = new MemoryStream();
        await NestedOrderParquetExtensions.WriteParquetAsync(rows, ms);
        ms.Position = 0;

        var back = await NestedOrderParquetExtensions.ReadParquetAsync(ms);

        Assert.Equal(3, back.Count);
        Assert.Equal("NYC", back[0].Ship!.City);
        Assert.Equal(10001, back[0].Ship!.Zip);
        Assert.Equal("Boston", back[0].Bill.City);
        Assert.Null(back[1].Ship);
        Assert.Null(back[1].Bill.City);
        Assert.Null(back[1].Bill.Zip);
        Assert.Null(back[2].Ship!.City);
        Assert.Equal(55, back[2].Ship!.Zip);
        Assert.Equal("LA", back[2].Bill.City);
        Assert.Null(back[2].Bill.Zip);
    }

    [Fact]
    public async Task StructInStructRoundTrips()
    {
        var rows = new List<Envelope>
        {
            new()
            {
                Id = 0,
                Outer = new OuterInfo
                {
                    Label = "first",
                    Inner = new Address { City = "Denver", Zip = 80001 },
                },
            },
            new()
            {
                Id = 1,
                Outer = new OuterInfo { Label = null, Inner = null },
            },
            new() { Id = 2, Outer = null },
        };

        var ms = new MemoryStream();
        await EnvelopeParquetExtensions.WriteParquetAsync(rows, ms);
        ms.Position = 0;
        var back = await EnvelopeParquetExtensions.ReadParquetAsync(ms);

        Assert.Equal("first", back[0].Outer!.Label);
        Assert.Equal("Denver", back[0].Outer!.Inner!.City);
        Assert.Null(back[1].Outer!.Label);
        Assert.Null(back[1].Outer!.Inner);
        Assert.Null(back[2].Outer);
    }

    [Fact]
    public async Task ArrayAndStreamPathsAgreeWithListRead()
    {
        var rows = new List<NestedOrder>
        {
            new()
            {
                Id = 0,
                Ship = new Address { City = "A", Zip = 1 },
                Bill = new Address { City = "B", Zip = 2 },
            },
            new()
            {
                Id = 1,
                Ship = null,
                Bill = new Address(),
            },
        };

        var ms = new MemoryStream();
        await rows.WriteParquetAsync(ms);
        ms.Position = 0;
        var array = await NestedOrderParquetExtensions.ReadParquetArrayAsync(ms);

        Assert.Equal(2, array.Length);
        Assert.Equal("A", array[0].Ship!.City);
        Assert.Null(array[1].Ship);
    }

    [Fact]
    public async Task AllStructsNullColumnDecodesAsAllNull()
    {
        var rows = new List<NestedOrder>
        {
            new()
            {
                Id = 0,
                Ship = null,
                Bill = new Address(),
            },
            new()
            {
                Id = 1,
                Ship = null,
                Bill = new Address(),
            },
        };

        var ms = new MemoryStream();
        await NestedOrderParquetExtensions.WriteParquetAsync(rows, ms);
        ms.Position = 0;
        var back = await NestedOrderParquetExtensions.ReadParquetAsync(ms);
        Assert.All(back, r => Assert.Null(r.Ship));
    }

    [Fact]
    public async Task EveryPublicSurfaceHandlesCompoundModels()
    {
        var rows = new List<NestedOrder>
        {
            new()
            {
                Id = 0,
                Ship = new Address { City = "X", Zip = 1 },
                Bill = new Address { City = "Y", Zip = 2 },
            },
            new()
            {
                Id = 1,
                Ship = null,
                Bill = new Address(),
            },
            new()
            {
                Id = 2,
                Ship = new Address { City = null, Zip = null },
                Bill = new Address { City = "Z", Zip = null },
            },
        };

        // IEnumerable batched write (fallback extraction path)
        var ms = new MemoryStream();
        await System
            .Linq.Enumerable.AsEnumerable(rows)
            .WriteParquetBatchedAsync(ms, new ParquetSerializerOptions { RowGroupSize = 2 });
        ms.Position = 0;

        // stream read
        ms.Position = 0;
        var streamed = new List<NestedOrder>();
        await foreach (NestedOrder r in NestedOrderParquetExtensions.ReadParquetStreamAsync(ms))
            streamed.Add(r);
        Assert.Equal(3, streamed.Count);
        Assert.Equal("X", streamed[0].Ship!.City);
        Assert.Null(streamed[1].Ship);
        Assert.Null(streamed[2].Ship!.City);
        Assert.Equal("Z", streamed[2].Bill.City);

        // parallel array read
        ms.Position = 0;
        var parallel = await NestedOrderParquetExtensions.ReadParquetParallelArrayAsync(ms);
        Assert.Equal(3, parallel.Length);
        Assert.Equal("Y", parallel[0].Bill.City);

        // memory buffer reads
        byte[] bytes = ms.ToArray();
        var mem = await NestedOrderParquetExtensions.ReadParquetAsync(bytes);
        Assert.Equal(3, mem.Count);
        var memArr = await NestedOrderParquetExtensions.ReadParquetParallelArrayAsync(bytes);
        Assert.Equal("X", memArr[0].Ship!.City);
        var memStream = new List<NestedOrder>();
        await foreach (NestedOrder r in NestedOrderParquetExtensions.ReadParquetStreamAsync(bytes))
            memStream.Add(r);
        Assert.Equal(3, memStream.Count);
    }

    [Fact]
    public async Task AsyncEnumerableWriteStreamReadRoundTrips()
    {
        var ms = new MemoryStream();
        async System.Collections.Generic.IAsyncEnumerable<NestedOrder> Source()
        {
            yield return new NestedOrder
            {
                Id = 0,
                Ship = new Address { City = "S", Zip = 9 },
                Bill = new Address(),
            };
            await Task.Yield();
        }

        await Source().WriteParquetAsync(ms);
        ms.Position = 0;
        var back = await NestedOrderParquetExtensions.ReadParquetAsync(ms);
        Assert.Equal("S", back[0].Ship!.City);
    }
}

[ParquetSerializable]
public sealed partial record Address
{
    public string? City { get; init; }
    public int? Zip { get; init; }
}

[ParquetSerializable]
public sealed partial record OuterInfo
{
    public string? Label { get; init; }
    public Address? Inner { get; init; }
}

[ParquetSerializable]
public sealed partial record NestedOrder
{
    public int Id { get; init; }
    public Address? Ship { get; init; }
    public Address Bill { get; init; } = new();
}

[ParquetSerializable]
public sealed partial record Envelope
{
    public int Id { get; init; }
    public OuterInfo? Outer { get; init; }
}
