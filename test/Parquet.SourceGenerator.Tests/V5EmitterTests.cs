extern alias V5Generator;

using Xunit;

namespace Parquet.SourceGenerator.Tests;

public class V5EmitterTests
{
    [Fact]
    public void V5CodeEmitterEmitsValidDataColumnBasedCode()
    {
        var properties = new V5Generator::Parquet.SourceGenerator.Models.PropertyModel[]
        {
            new("Id", "id", "int", null, null, 1, null, null, V5Generator::Parquet.SourceGenerator.Models.PropertyKind.Primitive, false),
            new("Name", "name", "string", null, null, 2, null, null, V5Generator::Parquet.SourceGenerator.Models.PropertyKind.Primitive, true),
        };

        var propArray = new V5Generator::Parquet.SourceGenerator.Models.EquatableArray<V5Generator::Parquet.SourceGenerator.Models.PropertyModel>(properties);
        var model = new V5Generator::Parquet.SourceGenerator.Models.TargetClassModel("TestNamespace", "TestModel", propArray);
        string code = V5Generator::Parquet.SourceGenerator.V5.Emitter.V5CodeEmitter.EmitSource(model);

        Assert.Contains("namespace TestNamespace;", code);
        Assert.Contains("public static partial class TestModelParquetV5Extensions", code);
        Assert.Contains("public static readonly global::Parquet.Schema.ParquetSchema Schema = new global::Parquet.Schema.ParquetSchema(", code);
        Assert.Contains("new global::Parquet.Data.DataColumn(_field_0, colArray_0)", code);
        Assert.Contains("rgWriter.WriteColumnAsync(col_0, cancellationToken)", code);
        Assert.Contains("rgReader.ReadColumnAsync(resolvedField_0, cancellationToken)", code);
    }
}
