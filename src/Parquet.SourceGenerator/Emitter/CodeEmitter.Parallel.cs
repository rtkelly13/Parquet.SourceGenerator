using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter;

public static partial class CodeEmitter
{
    // ──────────────────────────────────────────────────────────
    //  READ PARALLEL (Multi-Core with options)
    // ──────────────────────────────────────────────────────────

    private static void EmitReadParallelAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects from a Parquet stream, materialising");
        builder.AppendLine("    /// into a single pre-sized array indexed by row-group offset rather than growing a list.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    /// <remarks>");
        builder.AppendLine("    /// <para>");
        builder.AppendLine("    /// This overload reads row groups sequentially. A single <c>ParquetReader</c> over one");
        builder.AppendLine("    /// <c>Stream</c> cannot be read concurrently — the reader seeks within the stream, so overlapping");
        builder.AppendLine("    /// row-group reads corrupt each other — and an arbitrary <c>Stream</c> cannot be handed to more");
        builder.AppendLine("    /// than one reader.");
        builder.AppendLine("    /// </para>");
        builder.AppendLine("    /// <para>");
        builder.AppendLine("    /// For genuine decode parallelism use the <c>ReadOnlyMemory&lt;byte&gt;</c> overload, which gives");
        builder.AppendLine("    /// every worker its own reader over its own view of the same bytes.");
        builder.AppendLine("    /// <paramref name=\"maxDegreeOfParallelism\"/> is not honoured here; it is accepted so that the two");
        builder.AppendLine("    /// overloads share a signature.");
        builder.AppendLine("    /// </para>");
        builder.AppendLine("    /// </remarks>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{model.ClassName}>> ReadParquetParallelAsync(");
        builder.AppendLine($"        global::System.IO.Stream stream,");
        builder.AppendLine($"        int maxDegreeOfParallelism = -1,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (stream == null) throw new global::System.ArgumentNullException(nameof(stream));");
        builder.AppendLine();
        // No `targetParallelism` local here any more. It was computed from maxDegreeOfParallelism
        // and options.MaxDegreeOfParallelism and then never read — dead code that made the method
        // look as though it dispatched work it never dispatched.
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine();
        builder.AppendLine("        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream, BuildFormatOptions(options), cancellationToken: cancellationToken);");
        builder.AppendLine("        int rgCount = reader.RowGroupCount;");
        builder.AppendLine($"        if (rgCount == 0) return new global::System.Collections.Generic.List<{model.ClassName}>();");
        builder.AppendLine();
        builder.AppendLine("        int totalRows = (int)global::System.Linq.Enumerable.Sum(reader.RowGroups, rg => rg.RowCount);");
        builder.AppendLine($"        var resultArray = new {model.ClassName}[totalRows];");
        builder.AppendLine("        var rowOffsets = new int[rgCount];");
        builder.AppendLine("        int currentOffset = 0;");
        builder.AppendLine("        for (int r = 0; r < rgCount; r++)");
        builder.AppendLine("        {");
        builder.AppendLine("            rowOffsets[r] = currentOffset;");
        builder.AppendLine("            currentOffset += (int)reader.RowGroups[r].RowCount;");
        builder.AppendLine("        }");
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
        }
        builder.AppendLine();
        builder.AppendLine("        for (int r = 0; r < rgCount; r++)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            using var groupReader = reader.OpenRowGroupReader(r);");
        builder.AppendLine("            int rowCount = (int)groupReader.RowCount;");
        builder.AppendLine("            int startIdx = rowOffsets[r];");
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
        builder.AppendLine($"                    resultArray[startIdx + i] = new {model.ClassName}");
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
        builder.AppendLine();
        builder.AppendLine($"        return new global::System.Collections.Generic.List<{model.ClassName}>(resultArray);");
        builder.AppendLine("    }");
    }


    // ──────────────────────────────────────────────────────────
    //  READ PARALLEL OVER A BUFFER (one reader and one stream per worker)
    // ──────────────────────────────────────────────────────────

    private static void EmitReadParallelBufferAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Asynchronously deserializes all <c>{model.ClassName}</c> objects from an in-memory byte buffer,");
        builder.AppendLine("    /// decoding row groups across multiple workers.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    /// <remarks>");
        builder.AppendLine("    /// <para>");
        builder.AppendLine("    /// Each worker gets its own <c>ParquetReader</c> over its own stream across the same buffer, so");
        builder.AppendLine("    /// decompression and decoding — where the cost actually is — happen concurrently. This is the");
        builder.AppendLine("    /// reason the parallel reader is offered on a buffer and not on an arbitrary <c>Stream</c>: a");
        builder.AppendLine("    /// single reader seeks within its stream, so overlapping row-group reads on one would corrupt");
        builder.AppendLine("    /// each other.");
        builder.AppendLine("    /// </para>");
        builder.AppendLine("    /// <para>");
        builder.AppendLine("    /// Parallelism is bounded by the row-group count — a single-row-group file has nothing to divide");
        builder.AppendLine("    /// and reads sequentially. Row groups are claimed dynamically rather than split into fixed");
        builder.AppendLine("    /// ranges, so uneven group sizes do not leave workers idle. Results are ordered as in the file.");
        builder.AppendLine("    /// </para>");
        builder.AppendLine("    /// </remarks>");
        builder.AppendLine("    /// <param name=\"parquetBytes\">The complete Parquet file.</param>");
        builder.AppendLine("    /// <param name=\"maxDegreeOfParallelism\">");
        builder.AppendLine("    /// Maximum concurrent workers. Non-positive means unset, in which case");
        builder.AppendLine("    /// <c>ParquetSerializerOptions.MaxDegreeOfParallelism</c> applies, and failing that");
        builder.AppendLine("    /// <c>Environment.ProcessorCount</c>.");
        builder.AppendLine("    /// </param>");
        builder.AppendLine("    /// <param name=\"options\">Serializer options, or <c>null</c> for the defaults.</param>");
        builder.AppendLine("    /// <param name=\"cancellationToken\">Cancels the read.</param>");
        builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{model.ClassName}>> ReadParquetParallelAsync(");
        builder.AppendLine($"        global::System.ReadOnlyMemory<byte> parquetBytes,");
        builder.AppendLine($"        int maxDegreeOfParallelism = -1,");
        builder.AppendLine($"        global::Parquet.SourceGenerator.ParquetSerializerOptions? options = null,");
        builder.AppendLine($"        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine("        var formatOptions = BuildFormatOptions(options);");
        builder.AppendLine();
        // Every worker below builds its own stream over this buffer, and CreateBufferStream copies
        // whenever the memory is not array-backed. Left alone, a buffer from a MemoryManager or from
        // native memory would therefore be copied once per worker plus once for the probe — the file
        // materialised N+1 times, which for a large file is exactly the allocation profile the
        // buffer overload exists to avoid. Normalising once here makes every one of those calls take
        // the no-copy path. Array-backed buffers, which is what File.ReadAllBytes and
        // MemoryStream.ToArray give you, pay nothing for this check.
        builder.AppendLine("        var sourceBytes = global::System.Runtime.InteropServices.MemoryMarshal.TryGetArray(parquetBytes, out _)");
        builder.AppendLine("            ? parquetBytes");
        builder.AppendLine("            : new global::System.ReadOnlyMemory<byte>(parquetBytes.ToArray());");
        builder.AppendLine();
        // One cheap footer read decides the layout. Row counts come from row-group metadata, so this
        // probe does not touch column data — but it has to happen before the result array can be
        // sized, and it has to happen once rather than once per worker.
        builder.AppendLine("        int rowGroupCount;");
        builder.AppendLine("        int totalRows = 0;");
        builder.AppendLine("        int[] rowOffsets;");
        builder.AppendLine("        using (var probeStream = CreateBufferStream(sourceBytes))");
        builder.AppendLine("        {");
        builder.AppendLine("            await using var probe = await global::Parquet.ParquetReader.CreateAsync(probeStream, formatOptions, cancellationToken: cancellationToken);");
        builder.AppendLine("            rowGroupCount = probe.RowGroupCount;");
        builder.AppendLine("            rowOffsets = new int[rowGroupCount];");
        builder.AppendLine("            for (int r = 0; r < rowGroupCount; r++)");
        builder.AppendLine("            {");
        builder.AppendLine("                rowOffsets[r] = totalRows;");
        builder.AppendLine("                totalRows += (int)probe.RowGroups[r].RowCount;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        if (rowGroupCount == 0) return new global::System.Collections.Generic.List<{model.ClassName}>();");
        builder.AppendLine();
        builder.AppendLine($"        var resultArray = new {model.ClassName}[totalRows];");
        // A one-element array rather than a field or a StrongBox: Interlocked needs a `ref` to
        // storage that outlives the closure, and this keeps the whole thing allocation-cheap and
        // AOT-friendly.
        builder.AppendLine("        var cursor = new int[1];");
        builder.AppendLine();
        builder.AppendLine("        int requested = maxDegreeOfParallelism > 0");
        builder.AppendLine("            ? maxDegreeOfParallelism");
        builder.AppendLine("            : (options.MaxDegreeOfParallelism > 0 ? options.MaxDegreeOfParallelism : global::System.Environment.ProcessorCount);");
        builder.AppendLine("        int workerCount = global::System.Math.Max(1, global::System.Math.Min(requested, rowGroupCount));");
        builder.AppendLine();
        builder.AppendLine("        if (workerCount == 1)");
        builder.AppendLine("        {");
        // Staying on the calling thread when there is nothing to divide avoids paying a thread-pool
        // hop and a second reader for a single-row-group file, which is the common small-file case.
        builder.AppendLine("            await ReadRowGroupsIntoAsync(sourceBytes, formatOptions, resultArray, rowOffsets, cursor, rowGroupCount, cancellationToken);");
        builder.AppendLine("        }");
        builder.AppendLine("        else");
        builder.AppendLine("        {");
        builder.AppendLine("            var workers = new global::System.Threading.Tasks.Task[workerCount];");
        builder.AppendLine("            for (int w = 0; w < workerCount; w++)");
        builder.AppendLine("            {");
        // Task.Run is load-bearing, not ceremony. Reads over a MemoryStream complete synchronously,
        // so an async method invoked directly would run to completion on the calling thread and the
        // "parallel" loop would be sequential. Task.Run is what puts each worker on its own thread.
        builder.AppendLine("                workers[w] = global::System.Threading.Tasks.Task.Run(");
        builder.AppendLine("                    () => ReadRowGroupsIntoAsync(sourceBytes, formatOptions, resultArray, rowOffsets, cursor, rowGroupCount, cancellationToken),");
        builder.AppendLine("                    cancellationToken);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            await global::System.Threading.Tasks.Task.WhenAll(workers);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        return new global::System.Collections.Generic.List<{model.ClassName}>(resultArray);");
        builder.AppendLine("    }");
        builder.AppendLine();

        EmitReadRowGroupsIntoAsync(builder, model);
    }

    private static void EmitReadRowGroupsIntoAsync(StringBuilder builder, TargetClassModel model)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// One parallel-read worker: opens its own reader over the shared buffer and materialises every");
        builder.AppendLine("    /// row group it manages to claim.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    /// <remarks>");
        builder.AppendLine("    /// Workers write into disjoint index ranges of <paramref name=\"target\"/> — a row group's rows");
        builder.AppendLine("    /// start at its precomputed offset — so no synchronisation is needed beyond claiming the group.");
        builder.AppendLine("    /// </remarks>");
        builder.AppendLine("    private static async global::System.Threading.Tasks.Task ReadRowGroupsIntoAsync(");
        builder.AppendLine("        global::System.ReadOnlyMemory<byte> parquetBytes,");
        builder.AppendLine("        global::Parquet.ParquetOptions formatOptions,");
        builder.AppendLine($"        {model.ClassName}[] target,");
        builder.AppendLine("        int[] rowOffsets,");
        builder.AppendLine("        int[] cursor,");
        builder.AppendLine("        int rowGroupCount,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");
        builder.AppendLine("        using var stream = CreateBufferStream(parquetBytes);");
        builder.AppendLine("        await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream, formatOptions, cancellationToken: cancellationToken);");
        builder.AppendLine();
        builder.AppendLine("        var fileFields = reader.Schema.DataFields;");

        if (model.Properties.Length > 0)
        {
            builder.AppendLine("        global::System.Collections.Generic.Dictionary<string, global::Parquet.Schema.DataField>? fieldsByName = null;");
            for (int i = 0; i < model.Properties.Length; i++)
            {
                builder.AppendLine($"        var field_{i} = ResolveSchemaField(fileFields, {i}, _field_{i}, ref fieldsByName);");
            }
        }

        builder.AppendLine();
        builder.AppendLine("        while (true)");
        builder.AppendLine("        {");
        builder.AppendLine("            int r = global::System.Threading.Interlocked.Increment(ref cursor[0]) - 1;");
        builder.AppendLine("            if (r >= rowGroupCount) break;");
        builder.AppendLine();
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            using var groupReader = reader.OpenRowGroupReader(r);");
        builder.AppendLine("            int rowCount = (int)groupReader.RowCount;");
        builder.AppendLine("            int startIdx = rowOffsets[r];");
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
            string readCall = GetReadPrimitiveCall(prop, $"field_{i}", $"buffer_{i}");
            builder.AppendLine($"                {readCall}");
        }

        builder.AppendLine();
        builder.AppendLine("                for (int i = 0; i < rowCount; i++)");
        builder.AppendLine("                {");
        builder.AppendLine($"                    target[startIdx + i] = new {model.ClassName}");
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
            string clearArg = IsReferenceTypeBuffer(prop) ? "clearArray: true" : "clearArray: false";
            builder.AppendLine($"                global::System.Buffers.ArrayPool<{bufType}>.Shared.Return(buffer_{i}, {clearArg});");
        }

        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }


}
