using System.Text;
using Parquet.SourceGenerator.Emitter.Components;
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
        builder.AppendLine("        int totalRows = (int)global::System.Linq.Enumerable.Sum(reader.RowGroups, rg => rg.RowCount);");
        builder.AppendLine("#if NET8_0_OR_GREATER");
        builder.AppendLine($"        var results = new global::System.Collections.Generic.List<{model.ClassName}>(totalRows);");
        builder.AppendLine("        global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(results, totalRows);");
        builder.AppendLine("#else");
        builder.AppendLine($"        var results = new global::System.Collections.Generic.List<{model.ClassName}>(totalRows);");
        builder.AppendLine("#endif");
        builder.AppendLine("        int currentOffset = 0;");
        builder.AppendLine();
        builder.AppendLine("        var fileFields = reader.Schema.DataFields;");
        builder.AppendLine();

        // Schema field resolution against the file's actual fields (see ResolveSchemaField).
        if (model.Properties.Length > 0)
        {
            builder.AppendLine("        global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? fieldsByName = null;");
            for (int i = 0; i < model.Properties.Length; i++)
            {
                builder.AppendLine($"        var field_{i} = ResolveSchemaField(fileFields, {i}, _field_{i}, ref fieldsByName);");
            }
            builder.AppendLine();
        }

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
        builder.AppendLine("#if NET8_0_OR_GREATER");
        builder.AppendLine("                void PopulateSpan()");
        builder.AppendLine("                {");
        builder.AppendLine("                    var span = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(results);");
        builder.AppendLine("                    for (int i = 0; i < rowCount; i++)");
        builder.AppendLine("                    {");
        builder.AppendLine($"                        span[currentOffset + i] = new {model.ClassName}");
        builder.AppendLine("                        {");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string readExpr = GetReadExpression(prop, $"buffer_{i}[i]");
            builder.AppendLine($"                            {prop.Name} = {readExpr},");
        }

        builder.AppendLine("                        };");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine("                PopulateSpan();");
        builder.AppendLine("#else");
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
        builder.AppendLine("#endif");
        builder.AppendLine("                currentOffset += rowCount;");
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
    //  READ ARRAY (Zero-Copy Array Materialization)
    // ──────────────────────────────────────────────────────────

    private static void EmitReadArrayAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects directly into an array using Parquet.Net low-level primitives.");
        builder.AppendLine("    /// Eliminates List wrapper allocations for zero-copy array materialization.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<{model.ClassName}[]> ReadParquetArrayAsync(");
        builder.AppendLine($"        global::System.IO.Stream stream,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine();
        builder.AppendLine("        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine("        int totalRows = (int)global::System.Linq.Enumerable.Sum(reader.RowGroups, rg => rg.RowCount);");
        builder.AppendLine($"        var results = new {model.ClassName}[totalRows];");
        builder.AppendLine("        int currentOffset = 0;");
        builder.AppendLine();
        builder.AppendLine("        var fileFields = reader.Schema.DataFields;");
        builder.AppendLine();

        if (model.Properties.Length > 0)
        {
            builder.AppendLine("        global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? fieldsByName = null;");
            for (int i = 0; i < model.Properties.Length; i++)
            {
                builder.AppendLine($"        var field_{i} = ResolveSchemaField(fileFields, {i}, _field_{i}, ref fieldsByName);");
            }
            builder.AppendLine();
        }

        builder.AppendLine("        for (int r = 0; r < reader.RowGroupCount; r++)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            using var groupReader = reader.OpenRowGroupReader(r);");
        builder.AppendLine("            int rowCount = (int)groupReader.RowCount;");
        builder.AppendLine();

        BufferPoolComponent.EmitRentals(builder, model, "rowCount", indent: "            ");

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
        PropertyMappingComponent.EmitObjectMaterialization(builder, model, "results", "currentOffset", "i", indent: "                    ");
        builder.AppendLine("                }");
        builder.AppendLine("                currentOffset += rowCount;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");

        BufferPoolComponent.EmitReturns(builder, model, indent: "                ");

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
        builder.AppendLine("    /// Memory usage is bounded by a single row group rather than the whole file.");
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
            builder.AppendLine();
        }

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
        builder.AppendLine($"        var results = await ReadBufferSequentialArrayAsync(parquetBytes, options, cancellationToken);");
        builder.AppendLine($"        return new global::System.Collections.Generic.List<{model.ClassName}>(results);");
        builder.AppendLine("    }");
        builder.AppendLine();

        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects directly from an in-memory byte buffer into an array with zero buffer allocation.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<{model.ClassName}[]> ReadParquetArrayAsync(");
        builder.AppendLine($"        global::System.ReadOnlyMemory<byte> parquetBytes,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return await ReadBufferSequentialArrayAsync(parquetBytes, options, cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();

        // The buffer overload used to exist for ReadParquetAsync alone, so choosing the array-backed
        // or streaming reader meant giving up the zero-copy entry point and wrapping the bytes by
        // hand at the call site.
        EmitReadParallelBufferAsync(builder, model);
        builder.AppendLine();

        EmitReadBufferSequentialArrayAsync(builder, model);
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

    private static void EmitReadBufferSequentialArrayAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Dedicated sequential reader over an in-memory buffer without threadpool hops or stream wrapping overhead.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    private static async global::System.Threading.Tasks.Task<{model.ClassName}[]> ReadBufferSequentialArrayAsync(");
        builder.AppendLine($"        global::System.ReadOnlyMemory<byte> parquetBytes,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine("        using var stream = CreateBufferStream(parquetBytes);");
        builder.AppendLine("        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine("        int rowGroupCount = reader.RowGroupCount;");
        builder.AppendLine($"        if (rowGroupCount == 0) return global::System.Array.Empty<{model.ClassName}>();");
        builder.AppendLine();
        RowGroupLayoutComponent.EmitLayoutProbe(builder, "reader", "totalRows", "maxRowCount", "rowGroupCount", declareRowGroupCount: false);
        builder.AppendLine();
        builder.AppendLine($"        var results = new {model.ClassName}[totalRows];");
        builder.AppendLine("        int currentOffset = 0;");
        builder.AppendLine("        var fileFields = reader.Schema.DataFields;");
        builder.AppendLine("        global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? fieldsByName = null;");
        builder.AppendLine();

        for (int i = 0; i < model.Properties.Length; i++)
        {
            builder.AppendLine($"        var field_{i} = ResolveSchemaField(fileFields, {i}, _field_{i}, ref fieldsByName);");
        }

        builder.AppendLine();
        BufferPoolComponent.EmitRentals(builder, model, "maxRowCount");

        builder.AppendLine();
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            for (int r = 0; r < rowGroupCount; r++)");
        builder.AppendLine("            {");
        builder.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("                using var groupReader = reader.OpenRowGroupReader(r);");
        builder.AppendLine("                int rowCount = (int)groupReader.RowCount;");
        builder.AppendLine("                if (rowCount == 0) continue;");
        builder.AppendLine();

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string readCall = GetReadPrimitiveCall(prop, $"field_{i}", $"buffer_{i}");
            builder.AppendLine($"                {readCall}");
        }

        builder.AppendLine();
        builder.AppendLine("                for (int i = 0; i < rowCount; i++)");
        builder.AppendLine("                {");
        PropertyMappingComponent.EmitObjectMaterialization(builder, model, "results", "currentOffset", "i", indent: "                    ");
        builder.AppendLine("                }");
        builder.AppendLine("                currentOffset += rowCount;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");

        BufferPoolComponent.EmitReturns(builder, model);

        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return results;");
        builder.AppendLine("    }");
    }
}
