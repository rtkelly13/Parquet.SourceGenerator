using System.Text;
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
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        // The reader was previously created without options at all — the same defect audit item 3.1
        // closed on the v6 side, reintroduced here. v4's ParquetOptions carries no setting this
        // generator currently exposes, so this is plumbing rather than a behaviour change; what it
        // buys is that a read-relevant option added later reaches the reader instead of vanishing.
        builder.AppendLine("        using (var reader = await global::Parquet.ParquetReader.CreateAsync(stream, BuildFormatOptions(options), cancellationToken: cancellationToken).ConfigureAwait(false))");
        builder.AppendLine("        {");

        if (model.Properties.Length == 0)
        {
            builder.AppendLine($"            return new global::System.Collections.Generic.List<{model.ClassName}>();");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            return;
        }

        // Row counts come from the reader's row-group metadata. The previous form opened every row
        // group once to total the rows and then opened them all again to read — a second full pass
        // over the file whose only product was an int.
        builder.AppendLine("            int totalRows = 0;");
        builder.AppendLine("            for (int r = 0; r < reader.RowGroupCount; r++)");
        builder.AppendLine("            {");
        builder.AppendLine("                totalRows += (int)reader.RowGroups[r].RowCount;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine($"            var results = new global::System.Collections.Generic.List<{model.ClassName}>(totalRows);");
        builder.AppendLine();
        // Field resolution is per-file, not per-row-group. Doing it inside the loop also re-invoked
        // reader.Schema.GetDataFields(), which allocates a fresh array on every call.
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
        builder.AppendLine($"                        results.Add(new {model.ClassName}");
        builder.AppendLine("                        {");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            builder.AppendLine($"                            {prop.Name} = {GetReadExpression(prop, $"data_{i}[k]")},");
        }

        builder.AppendLine("                        });");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            return results;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

}
