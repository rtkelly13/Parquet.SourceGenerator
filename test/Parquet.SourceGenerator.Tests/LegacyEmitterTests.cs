extern alias LegacyGenerator;

using Xunit;

namespace Parquet.SourceGenerator.Tests;

public class LegacyEmitterTests
{
    [Fact]
    public void LegacyCodeEmitterEmitsValidDataColumnBasedCode()
    {
        var properties = new LegacyGenerator::Parquet.SourceGenerator.Models.PropertyModel[]
        {
            new("Id", "id", "int", null, null, 1, null, null, LegacyGenerator::Parquet.SourceGenerator.Models.PropertyKind.Primitive, false),
            new("Name", "name", "string", null, null, 2, null, null, LegacyGenerator::Parquet.SourceGenerator.Models.PropertyKind.Primitive, true),
        };

        var propArray = new LegacyGenerator::Parquet.SourceGenerator.Models.EquatableArray<LegacyGenerator::Parquet.SourceGenerator.Models.PropertyModel>(properties);
        var model = new LegacyGenerator::Parquet.SourceGenerator.Models.TargetClassModel("TestNamespace", "TestModel", propArray);
        string code = LegacyGenerator::Parquet.SourceGenerator.Legacy.Emitter.LegacyCodeEmitter.EmitSource(model);

        Assert.Contains("namespace TestNamespace;", code);
        Assert.Contains("public static partial class TestModelParquetLegacyExtensions", code);
        Assert.Contains("public static readonly global::Parquet.Schema.ParquetSchema Schema = new global::Parquet.Schema.ParquetSchema(", code);
        Assert.Contains("new global::Parquet.Data.DataColumn(_field_0, colArray_0)", code);
        Assert.Contains("rgWriter.WriteColumnAsync(col_0, cancellationToken)", code);
        Assert.Contains("rgReader.ReadColumnAsync(resolvedField_0, cancellationToken)", code);
    }
}
