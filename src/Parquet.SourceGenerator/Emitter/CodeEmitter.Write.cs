using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter;

public static partial class CodeEmitter
{
    // ──────────────────────────────────────────────────────────
    //  WRITE ROW GROUP (Low-Level Primitives, Static Field Access)
    // ──────────────────────────────────────────────────────────

    private static void EmitWriteRowGroupAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Writes a single row group chunk using Parquet.Net low-level primitives for maximum speed and Native AOT compatibility.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task WriteParquetRowGroupAsync(");
        builder.AppendLine($"        this global::Parquet.ParquetWriter writer,");
        builder.AppendLine($"        global::System.Collections.Generic.IReadOnlyCollection<{model.ClassName}> chunk,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (writer == null) throw new global::System.ArgumentNullException(nameof(writer));");
        builder.AppendLine("        if (chunk == null) throw new global::System.ArgumentNullException(nameof(chunk));");
        builder.AppendLine();
        builder.AppendLine("        int count = chunk.Count;");
        builder.AppendLine("        if (count == 0) return;");
        builder.AppendLine();

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = GetBufferElementType(prop);
            builder.AppendLine($"        var buffer_{i} = global::System.Buffers.ArrayPool<{bufType}>.Shared.Rent(count);");
        }

        builder.AppendLine();
        builder.AppendLine("        try");
        builder.AppendLine("        {");

        // List fast path
        builder.AppendLine($"            if (chunk is global::System.Collections.Generic.List<{model.ClassName}> listItems)");
        builder.AppendLine("            {");
        builder.AppendLine("                for (int i = 0; i < count; i++)");
        builder.AppendLine("                {");
        builder.AppendLine($"                    var item = listItems[i];");
        EmitPropertyAssignments(builder, model, "buffer_", prefix: "                    ");
        builder.AppendLine("                }");
        builder.AppendLine("            }");

        // Array fast path
        builder.AppendLine($"            else if (chunk is {model.ClassName}[] arrayItems)");
        builder.AppendLine("            {");
        builder.AppendLine("                for (int i = 0; i < count; i++)");
        builder.AppendLine("                {");
        builder.AppendLine($"                    var item = arrayItems[i];");
        EmitPropertyAssignments(builder, model, "buffer_", prefix: "                    ");
        builder.AppendLine("                }");
        builder.AppendLine("            }");

        // Enumerable fallback
        builder.AppendLine("            else");
        builder.AppendLine("            {");
        builder.AppendLine("                int idx = 0;");
        builder.AppendLine("                foreach (var item in chunk)");
        builder.AppendLine("                {");
        EmitPropertyAssignmentsIndexed(builder, model, "buffer_", prefix: "                    ");
        builder.AppendLine("                    idx++;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();

        // Write row group using static pre-allocated DataFields
        builder.AppendLine("            using (var groupWriter = writer.CreateRowGroup())");
        builder.AppendLine("            {");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string fieldAccess = $"_field_{i}";
            string writeCall = GetWritePrimitiveCall(prop, fieldAccess, $"buffer_{i}");
            builder.AppendLine($"                {writeCall}");
        }

        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = GetBufferElementType(prop);
            bool isRef = IsReferenceTypeBuffer(prop);
            string clearArg = isRef ? "clearArray: true" : "clearArray: false";
            builder.AppendLine($"            global::System.Buffers.ArrayPool<{bufType}>.Shared.Return(buffer_{i}, {clearArg});");
        }

        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    // ──────────────────────────────────────────────────────────
    //  WRITE (full collection with options)
    // ──────────────────────────────────────────────────────────

    private static void EmitWriteAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously serializes all <c>{model.ClassName}</c> items using Parquet.Net low-level primitives.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task WriteParquetAsync(");
        builder.AppendLine($"        this global::System.Collections.Generic.IReadOnlyCollection<{model.ClassName}> items,");
        builder.AppendLine($"        global::System.IO.Stream stream,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (items == null) throw new global::System.ArgumentNullException(nameof(items));");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine();
        builder.AppendLine("        await using var writer = await global::Parquet.ParquetWriter.CreateAsync(Schema, stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine("        await writer.WriteParquetRowGroupAsync(items, cancellationToken);");
        builder.AppendLine("    }");
    }

    // ──────────────────────────────────────────────────────────
    //  WRITE BATCHED (with options)
    // ──────────────────────────────────────────────────────────

    private static void EmitWriteBatchedAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Streams <c>{model.ClassName}</c> items into a Parquet file in fixed-size row group batches using low-level primitives.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task WriteParquetBatchedAsync(");
        builder.AppendLine($"        this global::System.Collections.Generic.IEnumerable<{model.ClassName}> items,");
        builder.AppendLine($"        global::System.IO.Stream stream,");
        builder.AppendLine($"        int? rowGroupSize = null,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (items == null) throw new global::System.ArgumentNullException(nameof(items));");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        EmitRowGroupSizeResolution(builder);
        builder.AppendLine();
        builder.AppendLine("        await using var writer = await global::Parquet.ParquetWriter.CreateAsync(Schema, stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine($"        var buffer = new global::System.Collections.Generic.List<{model.ClassName}>(targetChunkSize);");
        builder.AppendLine("        foreach (var item in items)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            buffer.Add(item);");
        builder.AppendLine("            if (buffer.Count == targetChunkSize)");
        builder.AppendLine("            {");
        builder.AppendLine("                await writer.WriteParquetRowGroupAsync(buffer, cancellationToken);");
        builder.AppendLine("                buffer.Clear();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        if (buffer.Count > 0)");
        builder.AppendLine("            await writer.WriteParquetRowGroupAsync(buffer, cancellationToken);");
        builder.AppendLine("    }");
    }

    // ──────────────────────────────────────────────────────────
    //  WRITE IAsyncEnumerable
    // ──────────────────────────────────────────────────────────

    private static void EmitWriteAsyncEnumerable(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Streams <c>{model.ClassName}</c> items asynchronously from an <c>IAsyncEnumerable&lt;{model.ClassName}&gt;</c> sequence into a Parquet file in fixed-size row group batches.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task WriteParquetAsync(");
        builder.AppendLine($"        this global::System.Collections.Generic.IAsyncEnumerable<{model.ClassName}> items,");
        builder.AppendLine($"        global::System.IO.Stream stream,");
        builder.AppendLine($"        int? rowGroupSize = null,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (items == null) throw new global::System.ArgumentNullException(nameof(items));");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        EmitRowGroupSizeResolution(builder);
        builder.AppendLine();
        builder.AppendLine("        await using var writer = await global::Parquet.ParquetWriter.CreateAsync(Schema, stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine($"        var buffer = new global::System.Collections.Generic.List<{model.ClassName}>(targetChunkSize);");
        builder.AppendLine("        await foreach (var item in items)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            buffer.Add(item);");
        builder.AppendLine("            if (buffer.Count == targetChunkSize)");
        builder.AppendLine("            {");
        builder.AppendLine("                await writer.WriteParquetRowGroupAsync(buffer, cancellationToken);");
        builder.AppendLine("                buffer.Clear();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        if (buffer.Count > 0)");
        builder.AppendLine("            await writer.WriteParquetRowGroupAsync(buffer, cancellationToken);");
        builder.AppendLine("    }");
    }


    private static void EmitRowGroupSizeResolution(StringBuilder builder)
    {
        // The old form was `options.RowGroupSize > 0 && options.RowGroupSize != 50_000 ? ... : rowGroupSize`,
        // which used the default value as a sentinel for "unset". Two consequences: setting
        // RowGroupSize to exactly 50,000 was indistinguishable from not setting it, and whenever
        // options *did* carry a size it silently overrode the explicit method argument — the more
        // specific value losing to the more general one. A nullable parameter says "unset" without
        // borrowing a legal value to mean it, so the precedence can be the obvious one: the explicit
        // argument wins, then options, then the options default.
        builder.AppendLine("        if (rowGroupSize.HasValue && rowGroupSize.Value <= 0)");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(rowGroupSize));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine("        int targetChunkSize = rowGroupSize ?? options.RowGroupSize;");
        builder.AppendLine("        if (targetChunkSize <= 0)");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(options), \"ParquetSerializerOptions.RowGroupSize must be greater than zero.\");");
    }


}
