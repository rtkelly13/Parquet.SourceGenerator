using System.Text;
using Parquet.SourceGenerator.Emitter.Components;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Legacy.Emitter;

public static partial class LegacyCodeEmitter
{
    private static void EmitReadAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects from a Parquet stream using Parquet.Net v4/v5.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{model.ClassName}>> ReadParquetAsync(");
        builder.AppendLine("        global::System.IO.Stream stream,");
        builder.AppendLine("        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine($"        var array = await ReadParquetArrayAsync(stream, options, cancellationToken);");
        builder.AppendLine($"        return new global::System.Collections.Generic.List<{model.ClassName}>(array);");
        builder.AppendLine("    }");
    }

    private static void EmitReadArrayAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects directly into an array using Parquet.Net v4/v5.");
        builder.AppendLine("    /// Eliminates List wrapper allocations for zero-copy array materialization.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<{model.ClassName}[]> ReadParquetArrayAsync(");
        builder.AppendLine("        global::System.IO.Stream stream,");
        builder.AppendLine("        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine("        using (var reader = await global::Parquet.ParquetReader.CreateAsync(stream, BuildFormatOptions(options), cancellationToken: cancellationToken).ConfigureAwait(false))");
        builder.AppendLine("        {");

        if (model.Properties.Length == 0)
        {
            builder.AppendLine($"            return global::System.Array.Empty<{model.ClassName}>();");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            return;
        }

        builder.AppendLine("            int totalRows = 0;");
        builder.AppendLine("            for (int r = 0; r < reader.RowGroupCount; r++)");
        builder.AppendLine("            {");
        builder.AppendLine("                totalRows += (int)reader.RowGroups[r].RowCount;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine($"            var results = new {model.ClassName}[totalRows];");
        builder.AppendLine("            int currentOffset = 0;");
        builder.AppendLine();
        builder.AppendLine("            var fileFields = reader.Schema.GetDataFields();");
        builder.AppendLine("            global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? fieldsByName = null;");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            builder.AppendLine($"            var field_{i} = ResolveSchemaField(fileFields, {i}, _field_{i}, ref fieldsByName);");
        }

        builder.AppendLine();
        builder.AppendLine("            for (int r = 0; r < reader.RowGroupCount; r++)");
        builder.AppendLine("            {");
        builder.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("                using (var rgReader = reader.OpenRowGroupReader(r))");
        builder.AppendLine("                {");
        builder.AppendLine("                    int groupRows = (int)rgReader.RowCount;");
        builder.AppendLine("                    if (groupRows == 0) continue;");
        builder.AppendLine();

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string elementType = GetColumnElementType(prop);
            builder.AppendLine($"                    var col_{i} = await rgReader.ReadColumnAsync(field_{i}, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine($"                    var data_{i} = ({elementType}[])col_{i}.Data;");
        }

        builder.AppendLine();
        builder.AppendLine("                    for (int k = 0; k < groupRows; k++)");
        builder.AppendLine("                    {");
        PropertyMappingComponent.EmitObjectMaterialization(builder, model, "results", "currentOffset", "k", bufferPrefix: "data_", indent: "                        ");
        builder.AppendLine("                    }");
        builder.AppendLine("                    currentOffset += groupRows;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            return results;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }
}

