using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter;

public static partial class CodeEmitter
{
    // ──────────────────────────────────────────────────────────
    //  READ (Sequential with options)
    // ──────────────────────────────────────────────────────────

    private static void EmitReadAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects using Parquet.Net low-level primitives.");
        builder.AppendLine("    /// Fast O(1) index-check schema resolution and ArrayPool buffer recycling.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{model.ClassName}>> ReadParquetAsync(");
        builder.AppendLine($"        global::System.IO.Stream stream,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine();
        builder.AppendLine("        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine($"        var results = new global::System.Collections.Generic.List<{model.ClassName}>((int)global::System.Linq.Enumerable.Sum(reader.RowGroups, rg => rg.RowCount));");
        builder.AppendLine();
        builder.AppendLine("        var fileFields = reader.Schema.DataFields;");

        // Schema field resolution against the file's actual fields (see ResolveSchemaField).
        if (model.Properties.Length > 0)
        {
            builder.AppendLine("        global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? fieldsByName = null;");
            for (int i = 0; i < model.Properties.Length; i++)
            {
                builder.AppendLine($"        var field_{i} = ResolveSchemaField(fileFields, {i}, _field_{i}, ref fieldsByName);");
            }
        }
        builder.AppendLine();

        builder.AppendLine("        for (int r = 0; r < reader.RowGroupCount; r++)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            using var groupReader = reader.OpenRowGroupReader(r);");
        builder.AppendLine("            int rowCount = (int)groupReader.RowCount;");
        builder.AppendLine();

        // Rent buffers for column reading
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = GetBufferElementType(prop);
            builder.AppendLine($"            var buffer_{i} = global::System.Buffers.ArrayPool<{bufType}>.Shared.Rent(rowCount);");
        }

        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        // Read columns using low-level primitive ReadAsync
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string fieldAccess = $"field_{i}";
            string readCall = GetReadPrimitiveCall(prop, fieldAccess, $"buffer_{i}");
            builder.AppendLine($"                {readCall}");
        }

        builder.AppendLine();
        // No Capacity assignment here. `results` is already constructed with capacity equal to the
        // file's total row count, and List<T>.Capacity reallocates whenever the assigned value
        // differs from the current backing array length — so setting it to the running total once
        // per row group allocated a *smaller* array and copied into it, repeatedly, before the list
        // grew back. Single-row-group files were unaffected; the multi-row-group files that
        // WriteParquetBatchedAsync produces paid O(groups x rows) of copying for nothing.
        builder.AppendLine("                for (int i = 0; i < rowCount; i++)");
        builder.AppendLine("                {");
        builder.AppendLine($"                    results.Add(new {model.ClassName}");
        builder.AppendLine("                    {");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string readExpr = GetReadExpression(prop, $"buffer_{i}[i]");
            builder.AppendLine($"                        {prop.Name} = {readExpr},");
        }

        builder.AppendLine("                    });");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = GetBufferElementType(prop);
            bool isRef = IsReferenceTypeBuffer(prop);
            string clearArg = isRef ? "clearArray: true" : "clearArray: false";
            builder.AppendLine($"                global::System.Buffers.ArrayPool<{bufType}>.Shared.Return(buffer_{i}, {clearArg});");
        }

        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return results;");
        builder.AppendLine("    }");
    }


    // ──────────────────────────────────────────────────────────
    //  READ STREAMING (IAsyncEnumerable)
    // ──────────────────────────────────────────────────────────

    private static void EmitReadStreamAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously streams <c>{model.ClassName}</c> items row-group by row-group as an <see cref=\"global::System.Collections.Generic.IAsyncEnumerable{{T}}\"/>.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Collections.Generic.IAsyncEnumerable<{model.ClassName}> ReadParquetStreamAsync(");
        builder.AppendLine($"        global::System.IO.Stream stream,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        [global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine();
        builder.AppendLine("        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine("        var fileFields = reader.Schema.DataFields;");
        builder.AppendLine();
        if (model.Properties.Length > 0)
        {
            builder.AppendLine("        global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? fieldsByName = null;");
            for (int i = 0; i < model.Properties.Length; i++)
            {
                builder.AppendLine($"        var field_{i} = ResolveSchemaField(fileFields, {i}, _field_{i}, ref fieldsByName);");
            }
        }
        builder.AppendLine();
        builder.AppendLine("        for (int r = 0; r < reader.RowGroupCount; r++)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            using var groupReader = reader.OpenRowGroupReader(r);");
        builder.AppendLine("            int rowCount = (int)groupReader.RowCount;");
        builder.AppendLine();
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = GetBufferElementType(prop);
            builder.AppendLine($"            var buffer_{i} = global::System.Buffers.ArrayPool<{bufType}>.Shared.Rent(rowCount);");
        }
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string fieldAccess = $"field_{i}";
            string readCall = GetReadPrimitiveCall(prop, fieldAccess, $"buffer_{i}");
            builder.AppendLine($"                {readCall}");
        }
        builder.AppendLine();
        builder.AppendLine("                for (int i = 0; i < rowCount; i++)");
        builder.AppendLine("                {");
        builder.AppendLine($"                    yield return new {model.ClassName}");
        builder.AppendLine("                    {");
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string readExpr = GetReadExpression(prop, $"buffer_{i}[i]");
            builder.AppendLine($"                        {prop.Name} = {readExpr},");
        }
        builder.AppendLine("                    };");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = GetBufferElementType(prop);
            bool isRef = IsReferenceTypeBuffer(prop);
            string clearArg = isRef ? "clearArray: true" : "clearArray: false";
            builder.AppendLine($"                global::System.Buffers.ArrayPool<{bufType}>.Shared.Return(buffer_{i}, {clearArg});");
        }
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }


    private static void EmitReadMemoryOverloads(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects directly from an in-memory byte buffer with zero buffer allocation.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{model.ClassName}>> ReadParquetAsync(");
        builder.AppendLine($"        global::System.ReadOnlyMemory<byte> parquetBytes,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        // The stream was previously created and handed off without ever being disposed (CA2000).
        // Awaiting inside a `using` keeps ownership here, where it belongs, rather than leaving it
        // to a caller who never sees the stream.
        builder.AppendLine("        using var stream = CreateBufferStream(parquetBytes);");
        builder.AppendLine("        return await ReadParquetAsync(stream, options, cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();

        // The buffer overload used to exist for ReadParquetAsync alone, so choosing the array-backed
        // or streaming reader meant giving up the zero-copy entry point and wrapping the bytes by
        // hand at the call site.
        EmitReadParallelBufferAsync(builder, model);
        builder.AppendLine();

        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously streams <c>{model.ClassName}</c> items from an in-memory byte buffer, row group by row group.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Collections.Generic.IAsyncEnumerable<{model.ClassName}> ReadParquetStreamAsync(");
        builder.AppendLine($"        global::System.ReadOnlyMemory<byte> parquetBytes,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        [global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        // Iterating inside the `using` rather than returning the inner sequence is what makes the
        // stream outlive exactly as long as enumeration does — including when the consumer breaks
        // out early, which disposes the iterator and so runs this `using`.
        builder.AppendLine("        using var stream = CreateBufferStream(parquetBytes);");
        builder.AppendLine("        await foreach (var item in ReadParquetStreamAsync(stream, options, cancellationToken))");
        builder.AppendLine("        {");
        builder.AppendLine("            yield return item;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();

        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Wraps a byte buffer as a read-only stream, without copying where the buffer is array-backed.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    /// <remarks>");
        builder.AppendLine("    /// Each call returns an independent stream over the same bytes, which is what makes the parallel");
        builder.AppendLine("    /// reader possible: every worker gets its own cursor without copying the file.");
        builder.AppendLine("    /// </remarks>");
        builder.AppendLine("    private static global::System.IO.MemoryStream CreateBufferStream(global::System.ReadOnlyMemory<byte> parquetBytes)");
        builder.AppendLine("    {");
        builder.AppendLine("        return global::System.Runtime.InteropServices.MemoryMarshal.TryGetArray(parquetBytes, out var segment)");
        builder.AppendLine("            ? new global::System.IO.MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)");
        builder.AppendLine("            : new global::System.IO.MemoryStream(parquetBytes.ToArray(), writable: false);");
        builder.AppendLine("    }");
    }


}
