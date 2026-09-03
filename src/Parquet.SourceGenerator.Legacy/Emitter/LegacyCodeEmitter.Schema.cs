using System.Text;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Legacy.Emitter;

public static partial class LegacyCodeEmitter
{
    private static void EmitSchema(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Static compile-time <c>Parquet.Schema.ParquetSchema</c> for <c>{model.ClassName}</c>.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    public static readonly global::Parquet.Schema.ParquetSchema Schema = new global::Parquet.Schema.ParquetSchema(");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string comma = i < model.Properties.Length - 1 ? "," : "";
            builder.AppendLine($"        {EmitterShared.GetFieldCreationExpression(prop)}{comma}");
        }

        builder.AppendLine("    );");
    }

    private static void EmitStaticFields(StringBuilder builder, TargetClassModel model)
    {
        EmitterShared.EmitStaticFields(builder, model);
    }

    private static void EmitResolveSchemaField(StringBuilder builder)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Resolves one generated schema field against the fields actually present in the file.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    private static global::Parquet.Schema.DataField ResolveSchemaField(");
        builder.AppendLine("        global::Parquet.Schema.DataField[] fileFields,");
        builder.AppendLine("        int index,");
        builder.AppendLine("        global::Parquet.Schema.DataField expected,");
        builder.AppendLine("        ref global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? byName)");
        builder.AppendLine("    {");
        builder.AppendLine("        string expectedPath = expected.Path.ToString();");
        builder.AppendLine();
        builder.AppendLine("        // Ordered schemas resolve on a single index check. Every file this generator writes lands");
        builder.AppendLine("        // here, as does any file whose column order matches; the linear scan below is only for");
        builder.AppendLine("        // files written with a different column order.");
        builder.AppendLine("        if ((uint)index < (uint)fileFields.Length");
        builder.AppendLine("            && string.Equals(fileFields[index].Path.ToString(), expectedPath, global::System.StringComparison.OrdinalIgnoreCase))");
        builder.AppendLine("        {");
        builder.AppendLine("            return fileFields[index];");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (byName is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            byName = new global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>(");
        builder.AppendLine("                fileFields.Length, global::System.StringComparer.OrdinalIgnoreCase);");
        builder.AppendLine("            for (int i = 0; i < fileFields.Length; i++)");
        builder.AppendLine("            {");
        builder.AppendLine("                // First occurrence wins if a file carries duplicate column names. Dictionary.TryAdd is");
        builder.AppendLine("                // not available to netstandard2.0 consumers, hence the explicit containment check.");
        builder.AppendLine("                string path = fileFields[i].Path.ToString();");
        builder.AppendLine("                if (!byName.ContainsKey(path))");
        builder.AppendLine("                {");
        builder.AppendLine("                    byName.Add(path, fileFields[i]);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (byName.TryGetValue(expectedPath, out var matched))");
        builder.AppendLine("        {");
        builder.AppendLine("            return matched;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (!expected.IsNullable)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.IO.InvalidDataException($\"Required column '{expectedPath}' was not found in the Parquet file schema.\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expected;");
        builder.AppendLine("    }");
    }

}
