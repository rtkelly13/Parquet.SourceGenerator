using System.Text;
using Parquet.SourceGenerator.Emitter.Components;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Legacy.Emitter;

public static partial class LegacyCodeEmitter
{
    private static void EmitSchema(StringBuilder builder, TargetClassModel model)
    {
        SchemaComponent.EmitSchema(builder, model);
    }

    private static void EmitStaticFields(StringBuilder builder, TargetClassModel model)
    {
        SchemaComponent.EmitStaticFields(builder, model);
    }

    private static void EmitResolveSchemaField(StringBuilder builder)
    {
        SchemaComponent.EmitResolveSchemaField(builder, usePath: true);
    }
}
