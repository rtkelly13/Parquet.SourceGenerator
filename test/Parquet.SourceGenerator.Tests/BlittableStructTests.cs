using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public readonly partial record struct SingleLongStruct
{
    [ParquetColumn("id")]
    public long Value { get; init; }
}

[ParquetSerializable]
public partial struct SingleDoubleStruct
{
    [ParquetColumn("val")]
    public double Value { get; set; }
}

[ParquetSerializable]
public partial struct Point3DStruct
{
    [ParquetColumn("x")]
    public double X { get; set; }

    [ParquetColumn("y")]
    public double Y { get; set; }

    [ParquetColumn("z")]
    public double Z { get; set; }
}

public sealed class BlittableStructTests
{
    [Fact]
    public async Task SingleLongStructListAndArrayRoundtrip()
    {
        const int count = 10_000;
        var items = new List<SingleLongStruct>(count);
        for (int i = 0; i < count; i++)
        {
            items.Add(new SingleLongStruct { Value = i * 42L });
        }

        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);

        // Read into List
        ms.Position = 0;
        var readList = await SingleLongStructParquetExtensions.ReadParquetAsync(ms);
        Assert.Equal(count, readList.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 42L, readList[i].Value);
        }

        // Read into Array
        ms.Position = 0;
        var readArray = await SingleLongStructParquetExtensions.ReadParquetArrayAsync(ms);
        Assert.Equal(count, readArray.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 42L, readArray[i].Value);
        }

        // Write from Array and Parallel Read
        using var ms2 = new MemoryStream();
        await readArray.WriteParquetAsync(ms2);
        var bytes = ms2.ToArray();

        var parallelArray = await SingleLongStructParquetExtensions.ReadParquetParallelArrayAsync(
            bytes
        );
        Assert.Equal(count, parallelArray.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 42L, parallelArray[i].Value);
        }

        var parallelList = await SingleLongStructParquetExtensions.ReadParquetParallelAsync(bytes);
        Assert.Equal(count, parallelList.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 42L, parallelList[i].Value);
        }
    }

    [Fact]
    public async Task SingleDoubleStructRoundtrip()
    {
        const int count = 5_000;
        var array = new SingleDoubleStruct[count];
        for (int i = 0; i < count; i++)
        {
            array[i] = new SingleDoubleStruct { Value = i * 3.14159 };
        }

        using var ms = new MemoryStream();
        await array.WriteParquetAsync(ms);

        ms.Position = 0;
        var readArray = await SingleDoubleStructParquetExtensions.ReadParquetArrayAsync(ms);
        Assert.Equal(count, readArray.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 3.14159, readArray[i].Value, precision: 5);
        }

        // Parallel read
        var parallelArray = await SingleDoubleStructParquetExtensions.ReadParquetParallelArrayAsync(
            ms.ToArray()
        );
        Assert.Equal(count, parallelArray.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 3.14159, parallelArray[i].Value, precision: 5);
        }
    }

    [Fact]
    public async Task MultiFieldStructPoint3DRoundtrip()
    {
        const int count = 1_000;
        var items = new List<Point3DStruct>(count);
        for (int i = 0; i < count; i++)
        {
            items.Add(
                new Point3DStruct
                {
                    X = i * 1.1,
                    Y = i * 2.2,
                    Z = i * 3.3,
                }
            );
        }

        using var ms = new MemoryStream();
        await items.WriteParquetAsync(ms);

        ms.Position = 0;
        var readArray = await Point3DStructParquetExtensions.ReadParquetArrayAsync(ms);
        Assert.Equal(count, readArray.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 1.1, readArray[i].X, precision: 5);
            Assert.Equal(i * 2.2, readArray[i].Y, precision: 5);
            Assert.Equal(i * 3.3, readArray[i].Z, precision: 5);
        }

        var bytes = ms.ToArray();
        var parallelRead = await Point3DStructParquetExtensions.ReadParquetParallelArrayAsync(
            bytes
        );
        Assert.Equal(count, parallelRead.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i * 1.1, parallelRead[i].X, precision: 5);
            Assert.Equal(i * 2.2, parallelRead[i].Y, precision: 5);
            Assert.Equal(i * 3.3, parallelRead[i].Z, precision: 5);
        }
    }
}
