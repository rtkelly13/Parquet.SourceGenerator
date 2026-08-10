using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Parquet.SourceGenerator.Diagnostics;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Models;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

public sealed class ParserAndEmitterTests
{
    private static readonly string[] ClassArgs1 = new[] { "TestClass" };
    private static readonly string[] ClassArgs2 = new[] { "col", "TestClass" };

    private static readonly int[] SampleArray1 = new[] { 1, 2, 3 };
    private static readonly int[] SampleArray2 = new[] { 1, 2, 3 };
    private static readonly int[] SampleArray3 = new[] { 1, 2, 4 };

    [Fact]
    public void CodeEmitterGeneratesValidSourceForComplexModel()
    {
        var properties = new[]
        {
            new PropertyModel("Id", "id", "int", null, null, 1, null, null, PropertyKind.Primitive, false),
            new PropertyModel("Name", "name", "string?", null, null, 2, null, null, PropertyKind.Primitive, true),
            new PropertyModel("Price", "price", "decimal", null, null, 3, 18, 4, PropertyKind.Decimal, false),
            new PropertyModel("CreatedAt", "created_at", "System.DateTime", "Microseconds", null, 4, null, null, PropertyKind.DateTime, false),
            new PropertyModel("Duration", "duration", "System.TimeSpan", null, null, 5, null, null, PropertyKind.TimeSpan, false),
            new PropertyModel("CorrelationId", "correlation_id", "System.Guid", null, null, 6, null, null, PropertyKind.Guid, true),
            new PropertyModel("Status", "status", "Parquet.SourceGenerator.Tests.EventStatus", null, "int", 7, null, null, PropertyKind.Enum, true),
            new PropertyModel("Data", "data", "byte[]", null, null, 8, null, null, PropertyKind.ByteArray, true),
        };

        var model = new TargetClassModel(
            Namespace: "TestNamespace",
            ClassName: "TestEntity",
            Properties: new EquatableArray<PropertyModel>(properties));

        string source = CodeEmitter.EmitSource(model);

        Assert.NotNull(source);
        Assert.Contains("namespace TestNamespace;", source);
        Assert.Contains("public static partial class TestEntityParquetExtensions", source);
        Assert.Contains("DecimalDataField", source);
        Assert.Contains("DateTimeDataField", source);
        Assert.Contains("TimeSpanDataField", source);
        Assert.Contains("WriteParquetRowGroupAsync", source);
        Assert.Contains("ReadParquetParallelAsync", source);
    }

    [Fact]
    public void EquatableArrayValueEqualityAndMethods()
    {
        var array1 = new EquatableArray<int>(SampleArray1);
        var array2 = new EquatableArray<int>(SampleArray2);
        var array3 = new EquatableArray<int>(SampleArray3);

        Assert.Equal(3, array1.Length);
        Assert.Equal(2, array1[1]);
        Assert.True(array1.Equals(array2));
        Assert.True(array1 == array2);
        Assert.False(array1 != array2);
        Assert.False(array1.Equals(array3));
        Assert.False(array1.Equals(null!));
        Assert.Equal(array1.GetHashCode(), array2.GetHashCode());
        Assert.Equal(0, EquatableArray<int>.Empty.Length);

        int sum = 0;
        foreach (int item in array1)
        {
            sum += item;
        }
        Assert.Equal(6, sum);
        Assert.Equal(3, array1.AsSpan().Length);
    }

    [Fact]
    public void DiagnosticInfoValueEqualityAndMethods()
    {
        var diag1 = new DiagnosticInfo(DiagnosticDescriptors.MustBePartial, Location.None, ClassArgs1);
        var diag2 = new DiagnosticInfo(DiagnosticDescriptors.MustBePartial, Location.None, ClassArgs1);
        var diag3 = new DiagnosticInfo(DiagnosticDescriptors.DuplicateColumnName, Location.None, ClassArgs2);

        Assert.True(diag1.Equals(diag2));
        Assert.False(diag1.Equals(diag3));
        Assert.Equal(diag1.GetHashCode(), diag2.GetHashCode());

        Diagnostic diagnostic = diag1.ToDiagnostic();
        Assert.Equal("PARQ001", diagnostic.Id);
    }

    // ── Parquet.Net Contract Enforcement Tests ─────────────────────────────

    [Fact]
    public async Task WriteParquetAsyncNullStreamThrowsArgumentNullException()
    {
        var items = new List<TypeCoverageRecord> { new() };
        await Assert.ThrowsAsync<ArgumentNullException>(() => items.WriteParquetAsync(null!));
    }

    [Fact]
    public async Task WriteParquetAsyncNullItemsThrowsArgumentNullException()
    {
        IReadOnlyCollection<TypeCoverageRecord> items = null!;
        var stream = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentNullException>(() => items.WriteParquetAsync(stream));
    }

    [Fact]
    public async Task WriteParquetBatchedAsyncInvalidRowGroupSizeThrowsArgumentOutOfRange()
    {
        var items = new List<TypeCoverageRecord> { new() };
        var stream = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => items.WriteParquetBatchedAsync(stream, rowGroupSize: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => items.WriteParquetBatchedAsync(stream, rowGroupSize: -10));
    }

    [Fact]
    public async Task ReadParquetAsyncNullStreamThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => TypeCoverageRecordParquetExtensions.ReadParquetAsync((Stream)null!));
    }

    [Fact]
    public async Task ReadParquetParallelAsyncNullStreamThrowsArgumentNullException()
    {
        // Cast required: ReadOnlyMemory<byte> has an implicit conversion from byte[], so a bare
        // `null` is convertible to the buffer overload as well as the stream one.
        await Assert.ThrowsAsync<ArgumentNullException>(() => TypeCoverageRecordParquetExtensions.ReadParquetParallelAsync((Stream)null!));
    }
}
