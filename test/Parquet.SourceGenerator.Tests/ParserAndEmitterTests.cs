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

    private static PropertyModel Prop(
        string name,
        string parquetColumnName,
        string typeName,
        int order,
        PropertyKind kind = PropertyKind.Primitive,
        bool isNullable = false,
        string? timestampUnit = null,
        string? enumUnderlyingTypeName = null,
        int? decimalPrecision = null,
        int? decimalScale = null,
        ColumnEncoding encoding = ColumnEncoding.Default
    ) =>
        new(
            name,
            parquetColumnName,
            typeName,
            timestampUnit,
            enumUnderlyingTypeName,
            order,
            decimalPrecision,
            decimalScale,
            kind,
            isNullable,
            Encoding: encoding
        );

    [Fact]
    public void CodeEmitterGeneratesValidSourceForComplexModel()
    {
        var properties = new[]
        {
            Prop("Id", "id", "int", 1),
            Prop("Name", "name", "string?", 2, isNullable: true),
            Prop(
                "Price",
                "price",
                "decimal",
                3,
                PropertyKind.Decimal,
                decimalPrecision: 18,
                decimalScale: 4
            ),
            Prop(
                "CreatedAt",
                "created_at",
                "System.DateTime",
                4,
                PropertyKind.DateTime,
                timestampUnit: "Microseconds"
            ),
            Prop("Duration", "duration", "System.TimeSpan", 5, PropertyKind.TimeSpan),
            Prop(
                "CorrelationId",
                "correlation_id",
                "System.Guid",
                6,
                PropertyKind.Guid,
                isNullable: true
            ),
            Prop(
                "Status",
                "status",
                "Parquet.SourceGenerator.Tests.EventStatus",
                7,
                PropertyKind.Enum,
                isNullable: true,
                enumUnderlyingTypeName: "int"
            ),
            Prop("Data", "data", "byte[]", 8, PropertyKind.ByteArray, isNullable: true),
        };

        var model = new TargetClassModel(
            Namespace: "TestNamespace",
            ClassName: "TestEntity",
            Properties: new EquatableArray<PropertyModel>(properties)
        );

        string source = CodeEmitter.EmitSource(model);

        Assert.NotNull(source);
        Assert.Contains("namespace TestNamespace;", source);
        Assert.Contains("public static partial class TestEntityParquetExtensions", source);
        Assert.Contains("DecimalDataField", source);
        Assert.Contains("DateTimeDataField", source);
        Assert.Contains("TimeDataField", source);
        Assert.Contains("WriteParquetRowGroupAsync", source);
        Assert.Contains("WriteAllPartsAsync", source);
        Assert.Contains("ReadParquetParallelAsync", source);
        Assert.Contains("if (missing_1 || chunkStats_1?.NullCount == rowCount)", source);
        Assert.Contains("global::System.Array.Clear(buffer_1, 0, rowCount);", source);
        Assert.Contains(
            "else\n                {\n                    await groupReader.ReadAsync(",
            source
        );
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
        var diag1 = new DiagnosticInfo(
            DiagnosticDescriptors.MustBePartial,
            Location.None,
            ClassArgs1
        );
        var diag2 = new DiagnosticInfo(
            DiagnosticDescriptors.MustBePartial,
            Location.None,
            ClassArgs1
        );
        var diag3 = new DiagnosticInfo(
            DiagnosticDescriptors.DuplicateColumnName,
            Location.None,
            ClassArgs2
        );

        Assert.True(diag1.Equals(diag2));
        Assert.False(diag1.Equals(diag3));
        Assert.Equal(diag1.GetHashCode(), diag2.GetHashCode());

        Diagnostic diagnostic = diag1.ToDiagnostic();
        Assert.Equal("PARQ001", diagnostic.Id);
    }

    [Fact]
    public void PropertyModelEncodingEqualityAndCodeEmission()
    {
        var propDefault = Prop("Id", "id", "int", 1);
        var propDelta = Prop("Id", "id", "int", 1, encoding: ColumnEncoding.DeltaBinaryPacked);
        var propDictionary = Prop("Tag", "tag", "string", 2, encoding: ColumnEncoding.Dictionary);
        var propByteSplit = Prop(
            "Value",
            "value",
            "double",
            3,
            encoding: ColumnEncoding.ByteSplitStream
        );

        Assert.Equal(ColumnEncoding.Default, propDefault.Encoding);
        Assert.Equal(ColumnEncoding.DeltaBinaryPacked, propDelta.Encoding);
        Assert.NotEqual(propDefault, propDelta);

        var targetClass = new TargetClassModel(
            Namespace: "TestEncodingNamespace",
            ClassName: "TestEncodingEntity",
            Properties: new EquatableArray<PropertyModel>(
                new[] { propDelta, propDictionary, propByteSplit }
            )
        );

        string source = CodeEmitter.EmitSource(targetClass);
        Assert.Contains(
            "formatOptions.ColumnEncodingHints[\"id\"] = global::Parquet.EncodingHint.DeltaBinaryPacked;",
            source
        );
        Assert.Contains(
            "formatOptions.ColumnEncodingHints[\"tag\"] = global::Parquet.EncodingHint.Dictionary;",
            source
        );
        Assert.Contains(
            "formatOptions.ColumnEncodingHints[\"value\"] = global::Parquet.EncodingHint.ByteSplitStream;",
            source
        );
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
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            items.WriteParquetBatchedAsync(
                stream,
                new ParquetSerializerOptions { RowGroupSize = 0 }
            )
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            items.WriteParquetBatchedAsync(
                stream,
                new ParquetSerializerOptions { RowGroupSize = -10 }
            )
        );
    }

    [Fact]
    public async Task ReadParquetAsyncNullStreamThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TypeCoverageRecordParquetExtensions.ReadParquetAsync((Stream)null!)
        );
    }

    [Fact]
    public async Task ReadParquetParallelAsyncNullStreamThrowsArgumentNullException()
    {
        // Cast required: ReadOnlyMemory<byte> has an implicit conversion from byte[], so a bare
        // `null` is convertible to the buffer overload as well as the stream one.
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TypeCoverageRecordParquetExtensions.ReadParquetParallelAsync((Stream)null!)
        );
    }
}
