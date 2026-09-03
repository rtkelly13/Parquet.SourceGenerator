using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Legacy.Emitter;

public static partial class LegacyCodeEmitter
{
    private static void EmitWriteRowGroupAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Writes a single row group chunk using Parquet.Net DataColumn primitives.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    public static async global::System.Threading.Tasks.Task WriteRowGroupAsync(");
        builder.AppendLine("        this global::Parquet.ParquetWriter writer,");
        builder.AppendLine($"        global::System.Collections.Generic.IReadOnlyList<{model.ClassName}> items,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (writer == null) throw new global::System.ArgumentNullException(nameof(writer));");
        builder.AppendLine("        if (items == null) throw new global::System.ArgumentNullException(nameof(items));");
        builder.AppendLine();

        if (model.Properties.Length == 0)
        {
            builder.AppendLine("        using (var rgWriter = writer.CreateRowGroup())");
            builder.AppendLine("        {");
            builder.AppendLine("            await global::System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            return;
        }

        builder.AppendLine("        int count = items.Count;");
        builder.AppendLine("        if (count == 0) return;");
        builder.AppendLine();

        // Array creation has to respect element rank. `byte[]` is the only element type here that is
        // itself an array, and the naive `new {elementType}[count]` produced `new byte[][count]`,
        // which is not C# — so every model with a byte[] column emitted a generated file that could
        // not parse. The rank suffix belongs after the length: `new byte[count][]`.
        for (int i = 0; i < model.Properties.Length; i++)
        {
            builder.AppendLine($"        var colArray_{i} = {GetArrayCreationExpression(model.Properties[i], "count")};");
        }

        builder.AppendLine();
        builder.AppendLine("        for (int k = 0; k < count; k++)");
        builder.AppendLine("        {");
        builder.AppendLine("            var item = items[k];");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            builder.AppendLine($"            colArray_{i}[k] = {GetWriteExpression(prop, $"item.{prop.Name}")};");
        }

        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        using (var rgWriter = writer.CreateRowGroup())");
        builder.AppendLine("        {");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            builder.AppendLine($"            var col_{i} = new global::Parquet.Data.DataColumn(_field_{i}, colArray_{i});");
            builder.AppendLine($"            await rgWriter.WriteColumnAsync(col_{i}, cancellationToken).ConfigureAwait(false);");
        }

        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void EmitWriteAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously serializes all <c>{model.ClassName}</c> items to stream using Parquet.Net v4/v5.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    public static async global::System.Threading.Tasks.Task WriteParquetAsync(");
        builder.AppendLine($"        this global::System.Collections.Generic.IReadOnlyList<{model.ClassName}> items,");
        builder.AppendLine("        global::System.IO.Stream stream,");
        builder.AppendLine("        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (items == null) throw new global::System.ArgumentNullException(nameof(items));");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine("        using (var writer = await global::Parquet.ParquetWriter.CreateAsync(Schema, stream, BuildFormatOptions(options), cancellationToken: cancellationToken).ConfigureAwait(false))");
        builder.AppendLine("        {");
        builder.AppendLine("            ApplyCompression(writer, options);");
        builder.AppendLine("            await writer.WriteRowGroupAsync(items, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void EmitWriteBatchedAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously serializes items in fixed-size row group chunks.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    public static async global::System.Threading.Tasks.Task WriteParquetBatchedAsync(");
        builder.AppendLine($"        this global::System.Collections.Generic.IEnumerable<{model.ClassName}> items,");
        builder.AppendLine("        global::System.IO.Stream stream,");
        builder.AppendLine("        int? rowGroupSize = null,");
        builder.AppendLine("        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (items == null) throw new global::System.ArgumentNullException(nameof(items));");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        // Same precedence rules the v6 emitter settled on in audit item 3.2: explicit argument wins,
        // then options, then the options default — and a non-positive value from either source is an
        // error rather than a silent fallback.
        builder.AppendLine("        if (rowGroupSize.HasValue && rowGroupSize.Value <= 0)");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(rowGroupSize));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine("        int batchSize = rowGroupSize ?? options.RowGroupSize;");
        builder.AppendLine("        if (batchSize <= 0)");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(options), \"ParquetSerializerOptions.RowGroupSize must be greater than zero.\");");
        builder.AppendLine();
        builder.AppendLine($"        if (items is global::System.Collections.Generic.IReadOnlyList<{model.ClassName}> list && list.Count <= batchSize)");
        builder.AppendLine("        {");
        builder.AppendLine("            using (var singleWriter = await global::Parquet.ParquetWriter.CreateAsync(Schema, stream, BuildFormatOptions(options), cancellationToken: cancellationToken).ConfigureAwait(false))");
        builder.AppendLine("            {");
        builder.AppendLine("                ApplyCompression(singleWriter, options);");
        builder.AppendLine("                await singleWriter.WriteRowGroupAsync(list, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            }");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        using (var writer = await global::Parquet.ParquetWriter.CreateAsync(Schema, stream, BuildFormatOptions(options), cancellationToken: cancellationToken).ConfigureAwait(false))");
        builder.AppendLine("        {");
        builder.AppendLine("            ApplyCompression(writer, options);");
        builder.AppendLine($"            var chunk = new global::System.Collections.Generic.List<{model.ClassName}>(batchSize);");
        builder.AppendLine("            foreach (var item in items)");
        builder.AppendLine("            {");
        builder.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("                chunk.Add(item);");
        builder.AppendLine("                if (chunk.Count >= batchSize)");
        builder.AppendLine("                {");
        builder.AppendLine("                    await writer.WriteRowGroupAsync(chunk, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("                    chunk.Clear();");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            if (chunk.Count > 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                await writer.WriteRowGroupAsync(chunk, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

}
