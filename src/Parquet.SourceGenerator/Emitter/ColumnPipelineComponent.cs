using System;
using System.Collections.Generic;
using System.Text;
using Parquet.SourceGenerator.Emitter.Components;
using Parquet.SourceGenerator.Emitter.Compound;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter;

/// <summary>
/// One physical buffer slot in the column-pipelined write plan: a pooled array of a single
/// element type, rented on the first column that needs it and returned after the last one.
/// </summary>
internal sealed class PipelineSlot
{
    /// <summary>Zero-based slot id; names the generated local (<c>slot_0</c>).</summary>
    public int Id { get; set; }

    /// <summary>Buffer element type, e.g. <c>long</c> or <c>global::System.ReadOnlyMemory&lt;char&gt;?</c>.</summary>
    public string ElementType { get; set; } = "";

    /// <summary>Estimated managed size of one element, in bytes.</summary>
    public int ElementSize { get; set; }

    /// <summary>Whether returning the buffer must clear it (it can hold object references).</summary>
    public bool ClearOnReturn { get; set; }

    /// <summary>Index into the column list of the first column using this slot.</summary>
    public int FirstUse { get; set; } = int.MaxValue;

    /// <summary>Index into the column list of the last column using this slot.</summary>
    public int LastUse { get; set; } = -1;
}

/// <summary>
/// The static type-reuse map for one model: which pooled buffer slot each column writes into,
/// and the resulting peak concurrent buffer footprint.
/// </summary>
internal sealed class PipelinePlan
{
    /// <summary>Distinct data buffer slots, in first-use order.</summary>
    public PipelineSlot[] Slots { get; set; } = [];

    /// <summary>Slot id used by each column, indexed by position in the emission plan.</summary>
    public int[] SlotByColumn { get; set; } = [];

    /// <summary>Column positions that also need the shared definition-level buffer.</summary>
    public bool[] NeedsDefLevels { get; set; } = [];

    /// <summary>First column position needing definition levels, or -1.</summary>
    public int DefFirstUse { get; set; } = -1;

    /// <summary>Last column position needing definition levels, or -1.</summary>
    public int DefLastUse { get; set; } = -1;

    /// <summary>Bytes of pooled column buffer per row held concurrently by the row-oriented path.</summary>
    public int RowOrientedBytesPerRow { get; set; }

    /// <summary>Peak bytes of pooled column buffer per row held concurrently by the pipelined path.</summary>
    public int PipelinedBytesPerRow { get; set; }

    /// <summary>ArrayPool rentals the row-oriented path performs per row group.</summary>
    public int RowOrientedRentals { get; set; }

    /// <summary>ArrayPool rentals the pipelined path performs per row group.</summary>
    public int PipelinedRentals { get; set; }
}

/// <summary>
/// Codegen-time construction and emission of the column-pipelined write path (issue #136).
/// </summary>
/// <remarks>
/// The row-oriented default rents every column buffer up front so a single pass over the rows can
/// fill them all while the row object is hot in L1/L2. That is 1.8x-5.5x faster (docs/12) but its
/// peak footprint is <c>N * batch * elementSize</c>. The pipelined path instead makes one pass per
/// column and reuses a single pooled buffer per distinct physical element type, so the peak is the
/// largest set of type slots that are simultaneously live under Parquet's fixed schema column
/// order — a slot must be held across intermediate columns of other types until the next column of
/// its own type is reached.
/// </remarks>
internal static class ColumnPipelineComponent
{
    /// <summary>
    /// Whether the column-pipelined path can be emitted for this model. Compound (struct) and
    /// list columns are excluded: their extraction carries repetition/definition ladders whose
    /// packed lanes are not a plain one-value-per-row column, so the type-slot reuse map does not
    /// apply unchanged.
    /// </summary>
    public static bool IsSupported(TargetClassModel model)
    {
        if (model.Properties.Length == 0)
            return false;
        EmissionPlan plan = EmissionPlan.For(model);
        if (plan.HasCompound)
            return false;
        foreach (LeafColumn col in plan.Columns)
        {
            if (col.IsCompound || col.IsListLeaf)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Builds the static reuse map. Safe to call for compound models too — the byte totals are
    /// still meaningful, only <see cref="IsSupported"/> gates emission of the pipelined method.
    /// </summary>
    public static PipelinePlan Build(TargetClassModel model)
    {
        LeafColumn[] columns = EmissionPlan.For(model).Columns;
        var slots = new List<PipelineSlot>();
        var byType = new Dictionary<string, PipelineSlot>(StringComparer.Ordinal);
        var slotByColumn = new int[columns.Length];
        var needsDef = new bool[columns.Length];
        var plan = new PipelinePlan();

        int rowBytes = 0;
        int rowRentals = 0;

        for (int c = 0; c < columns.Length; c++)
        {
            LeafColumn col = columns[c];
            PropertyModel prop = col.Leaf;
            bool compoundish = col.IsCompound || col.IsListLeaf;
            bool usesParts = compoundish || BufferPoolComponent.UsesWriteAllParts(prop);

            string elementType =
                compoundish ? col.PackedType
                : usesParts ? BufferPoolComponent.GetNonNullableBufferType(prop)
                : BufferPoolComponent.GetWriteBufferElementType(prop);

            int elementSize = EstimateElementSize(elementType);
            rowBytes += elementSize;
            rowRentals++;
            if (usesParts)
            {
                rowBytes += 4; // int[] defLevels
                rowRentals++;
            }
            if (col.IsListLeaf)
            {
                rowBytes += 4; // int[] repLevels
                rowRentals++;
            }

            needsDef[c] = usesParts;
            if (usesParts)
            {
                if (plan.DefFirstUse < 0)
                    plan.DefFirstUse = c;
                plan.DefLastUse = c;
            }

            if (!byType.TryGetValue(elementType, out PipelineSlot? slot))
            {
                slot = new PipelineSlot
                {
                    Id = slots.Count,
                    ElementType = elementType,
                    ElementSize = elementSize,
                    // Packed value lanes never hold references; string and byte[] columns do.
                    ClearOnReturn =
                        !usesParts
                        && BufferPoolComponent.IsReferenceTypeBuffer(prop, isWrite: true),
                };
                slots.Add(slot);
                byType[elementType] = slot;
            }

            slotByColumn[c] = slot.Id;
            if (c < slot.FirstUse)
                slot.FirstUse = c;
            slot.LastUse = c;
        }

        // Peak concurrent bytes: at each column position, sum the slots whose live range covers it.
        int peak = 0;
        for (int c = 0; c < columns.Length; c++)
        {
            int live = 0;
            foreach (PipelineSlot slot in slots)
            {
                if (slot.FirstUse <= c && c <= slot.LastUse)
                    live += slot.ElementSize;
            }
            if (plan.DefFirstUse >= 0 && plan.DefFirstUse <= c && c <= plan.DefLastUse)
                live += 4;
            if (live > peak)
                peak = live;
        }

        plan.Slots = slots.ToArray();
        plan.SlotByColumn = slotByColumn;
        plan.NeedsDefLevels = needsDef;
        plan.RowOrientedBytesPerRow = rowBytes;
        plan.PipelinedBytesPerRow = peak;
        plan.RowOrientedRentals = rowRentals;
        plan.PipelinedRentals = slots.Count + (plan.DefFirstUse >= 0 ? 1 : 0);
        return plan;
    }

    /// <summary>
    /// Managed size of one buffer element, in bytes, on a 64-bit runtime. Used only to scale the
    /// <c>Auto</c> heuristic, so an approximation is adequate; nullable value types add their
    /// alignment-rounded flag byte.
    /// </summary>
    public static int EstimateElementSize(string elementType)
    {
        string t = elementType;
        bool nullable = t.EndsWith("?", StringComparison.Ordinal);
        if (nullable)
            t = t.Substring(0, t.Length - 1);
        if (t.StartsWith("global::", StringComparison.Ordinal))
            t = t.Substring("global::".Length);
        if (t.StartsWith("System.", StringComparison.Ordinal))
            t = t.Substring("System.".Length);

        int size;
        int align;
        switch (t)
        {
            case "bool":
            case "Boolean":
            case "byte":
            case "Byte":
            case "sbyte":
            case "SByte":
                size = 1;
                align = 1;
                break;
            case "short":
            case "Int16":
            case "ushort":
            case "UInt16":
            case "char":
            case "Char":
                size = 2;
                align = 2;
                break;
            case "int":
            case "Int32":
            case "uint":
            case "UInt32":
            case "float":
            case "Single":
                size = 4;
                align = 4;
                break;
            case "decimal":
            case "Decimal":
                size = 16;
                align = 4;
                break;
            case "Guid":
                size = 16;
                align = 4;
                break;
            default:
                if (t.StartsWith("ReadOnlyMemory<", StringComparison.Ordinal))
                {
                    size = 16; // object reference + int index + int length
                    align = 8;
                }
                else
                {
                    // long, ulong, double, DateTime, DateTimeOffset, TimeSpan ticks, and any
                    // reference-typed buffer element all land here.
                    size = 8;
                    align = 8;
                }
                break;
        }

        return nullable ? size + align : size;
    }

    /// <summary>
    /// Emits the codegen-time constants describing both strategies' buffer footprints.
    /// </summary>
    public static void EmitConstants(StringBuilder builder, TargetClassModel model)
    {
        PipelinePlan plan = Build(model);
        bool supported = IsSupported(model);

        builder.AppendLine("    /// <summary>");
        builder.AppendLine(
            "    /// Whether a column-pipelined write path was generated for this model. False for models"
        );
        builder.AppendLine(
            "    /// with nested struct or list columns, whose packed lanes are not one value per row."
        );
        builder.AppendLine("    /// </summary>");
        builder.AppendLine(
            $"    public const bool SupportsColumnPipelinedWrite = {(supported ? "true" : "false")};"
        );
        builder.AppendLine();
        builder.AppendLine("    /// <summary>");
        builder.AppendLine(
            "    /// Estimated pooled column-buffer bytes held concurrently, per row, by the row-oriented path."
        );
        builder.AppendLine("    /// </summary>");
        builder.AppendLine(
            $"    public const int PeakRowOrientedBufferBytesPerRow = {plan.RowOrientedBytesPerRow};"
        );
        builder.AppendLine();
        builder.AppendLine("    /// <summary>");
        builder.AppendLine(
            "    /// Estimated peak pooled column-buffer bytes held concurrently, per row, by the"
        );
        builder.AppendLine(
            "    /// column-pipelined path under the static type-reuse map (equals the row-oriented"
        );
        builder.AppendLine("    /// figure when no pipelined path was generated).");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine(
            $"    public const int PeakColumnPipelinedBufferBytesPerRow = {(supported ? plan.PipelinedBytesPerRow : plan.RowOrientedBytesPerRow)};"
        );
        builder.AppendLine();
        builder.AppendLine(
            "    /// <summary>ArrayPool rentals the row-oriented path performs per row group.</summary>"
        );
        builder.AppendLine(
            $"    public const int RowOrientedBufferRentals = {plan.RowOrientedRentals};"
        );
        builder.AppendLine();
        builder.AppendLine(
            "    /// <summary>ArrayPool rentals the column-pipelined path performs per row group.</summary>"
        );
        builder.AppendLine(
            $"    public const int ColumnPipelinedBufferRentals = {(supported ? plan.PipelinedRentals : plan.RowOrientedRentals)};"
        );
    }

    /// <summary>
    /// Emits the strategy-dispatching <c>WriteParquetRowGroupAsync</c> overload that takes options.
    /// </summary>
    public static void EmitStrategyDispatch(StringBuilder builder, TargetClassModel model)
    {
        bool supported = IsSupported(model);

        builder.AppendLine("    /// <summary>");
        builder.AppendLine(
            "    /// Writes a single row group chunk, selecting the extraction strategy from <paramref name=\"options\"/>."
        );
        builder.AppendLine("    /// </summary>");
        builder.AppendLine(
            "    public static global::System.Threading.Tasks.Task WriteParquetRowGroupAsync("
        );
        builder.AppendLine("        this global::Parquet.ParquetWriter writer,");
        builder.AppendLine(
            $"        global::System.Collections.Generic.IReadOnlyCollection<{model.ClassName}> chunk,"
        );
        builder.AppendLine(
            "        global::Parquet.SourceGenerator.ParquetSerializerOptions? options,"
        );
        builder.AppendLine(
            "        global::System.Threading.CancellationToken cancellationToken = default)"
        );
        builder.AppendLine("    {");
        builder.AppendLine(
            "        if (writer == null) throw new global::System.ArgumentNullException(nameof(writer));"
        );
        builder.AppendLine(
            "        if (chunk == null) throw new global::System.ArgumentNullException(nameof(chunk));"
        );
        builder.AppendLine();

        if (!supported)
        {
            builder.AppendLine(
                "        // No column-pipelined path exists for this model; the strategy is advisory only."
            );
            builder.AppendLine(
                "        return writer.WriteParquetRowGroupAsync(chunk, cancellationToken);"
            );
            builder.AppendLine("    }");
            return;
        }

        builder.AppendLine(
            "        var strategy = options?.WriteStrategy ?? global::Parquet.SourceGenerator.ParquetWriteStrategy.RowOriented;"
        );
        builder.AppendLine(
            "        if (strategy == global::Parquet.SourceGenerator.ParquetWriteStrategy.Auto)"
        );
        builder.AppendLine("        {");
        builder.AppendLine(
            "            long thresholdBytes = options?.ColumnPipelinedMemoryThresholdBytes ?? (64L * 1024 * 1024);"
        );
        builder.AppendLine(
            "            long estimatedPeakBytes = (long)chunk.Count * PeakRowOrientedBufferBytesPerRow;"
        );
        builder.AppendLine("            strategy = estimatedPeakBytes > thresholdBytes");
        builder.AppendLine(
            "                ? global::Parquet.SourceGenerator.ParquetWriteStrategy.ColumnPipelined"
        );
        builder.AppendLine(
            "                : global::Parquet.SourceGenerator.ParquetWriteStrategy.RowOriented;"
        );
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine(
            "        return strategy == global::Parquet.SourceGenerator.ParquetWriteStrategy.ColumnPipelined"
        );
        builder.AppendLine(
            "            ? writer.WriteParquetRowGroupColumnPipelinedAsync(chunk, cancellationToken)"
        );
        builder.AppendLine(
            "            : writer.WriteParquetRowGroupAsync(chunk, cancellationToken);"
        );
        builder.AppendLine("    }");
    }

    /// <summary>
    /// Emits the column-pipelined row group writer: one traversal per column, one pooled buffer
    /// per distinct physical element type, held only across its own live range.
    /// </summary>
    public static void EmitColumnPipelinedWrite(StringBuilder builder, TargetClassModel model)
    {
        LeafColumn[] columns = EmissionPlan.For(model).Columns;
        PipelinePlan plan = Build(model);
        bool clearSource = !(model.IsValueType && model.IsUnmanaged);

        builder.AppendLine("    /// <summary>");
        builder.AppendLine(
            "    /// Writes a single row group chunk column by column, reusing one pooled buffer per distinct"
        );
        builder.AppendLine(
            "    /// physical element type. Produces byte-identical output to the row-oriented writer while"
        );
        builder.AppendLine(
            $"    /// holding at most {plan.PipelinedBytesPerRow} buffer bytes per row concurrently rather than {plan.RowOrientedBytesPerRow}."
        );
        builder.AppendLine("    /// </summary>");
        builder.AppendLine(
            "    public static async global::System.Threading.Tasks.Task WriteParquetRowGroupColumnPipelinedAsync("
        );
        builder.AppendLine("        this global::Parquet.ParquetWriter writer,");
        builder.AppendLine(
            $"        global::System.Collections.Generic.IReadOnlyCollection<{model.ClassName}> chunk,"
        );
        builder.AppendLine(
            "        global::System.Threading.CancellationToken cancellationToken = default)"
        );
        builder.AppendLine("    {");
        builder.AppendLine(
            "        if (writer == null) throw new global::System.ArgumentNullException(nameof(writer));"
        );
        builder.AppendLine(
            "        if (chunk == null) throw new global::System.ArgumentNullException(nameof(chunk));"
        );
        builder.AppendLine();
        builder.AppendLine("        int count = chunk.Count;");
        builder.AppendLine("        if (count == 0) return;");
        builder.AppendLine();
        builder.AppendLine(
            $"        var listItems = chunk as global::System.Collections.Generic.List<{model.ClassName}>;"
        );
        builder.AppendLine($"        var arrayItems = chunk as {model.ClassName}[];");
        builder.AppendLine($"        {model.ClassName}[]? rentedSource = null;");

        // Declare one nullable local per type slot, plus the shared definition-level buffer.
        foreach (PipelineSlot slot in plan.Slots)
        {
            builder.AppendLine($"        {slot.ElementType}[]? slot_{slot.Id} = null;");
        }
        if (plan.DefFirstUse >= 0)
        {
            builder.AppendLine("        int[]? defSlot = null;");
        }

        builder.AppendLine();
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            if (listItems is null && arrayItems is null)");
        builder.AppendLine("            {");
        builder.AppendLine(
            $"                rentedSource = global::System.Buffers.ArrayPool<{model.ClassName}>.Shared.Rent(count);"
        );
        builder.AppendLine("                int copyIndex = 0;");
        builder.AppendLine("                foreach (var sourceItem in chunk)");
        builder.AppendLine("                {");
        builder.AppendLine("                    rentedSource[copyIndex++] = sourceItem;");
        builder.AppendLine("                }");
        builder.AppendLine("                arrayItems = rentedSource;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            using (var groupWriter = writer.CreateRowGroup())");
        builder.AppendLine("            {");

        for (int c = 0; c < columns.Length; c++)
        {
            LeafColumn col = columns[c];
            PropertyModel prop = col.Leaf;
            PipelineSlot slot = plan.Slots[plan.SlotByColumn[c]];
            int i = col.Slot;
            bool usesParts = plan.NeedsDefLevels[c];

            builder.AppendLine(
                $"                // column {c}: {prop.Name} -> slot_{slot.Id} ({slot.ElementType})"
            );
            builder.AppendLine("                {");
            builder.AppendLine(
                $"                    {slot.ElementType}[] buffer_{i} = slot_{slot.Id} ??= global::System.Buffers.ArrayPool<{slot.ElementType}>.Shared.Rent(count);"
            );
            if (usesParts)
            {
                builder.AppendLine(
                    $"                    int[] defLevels_{i} = defSlot ??= global::System.Buffers.ArrayPool<int>.Shared.Rent(count);"
                );
                builder.AppendLine($"                    int nonNullCount_{i} = 0;");
            }

            EmitColumnPass(builder, col, i, usesParts, "                    ");

            builder.AppendLine(
                CodeEmitter.GetWritePrimitiveCallForPipeline(
                    col,
                    $"_field_{i}",
                    $"buffer_{i}",
                    "                    "
                )
            );
            builder.AppendLine("                }");

            if (plan.DefLastUse == c)
            {
                builder.AppendLine(
                    "                global::System.Buffers.ArrayPool<int>.Shared.Return(defSlot, clearArray: false);"
                );
                builder.AppendLine("                defSlot = null;");
            }
            if (slot.LastUse == c)
            {
                string clearArg = slot.ClearOnReturn ? "clearArray: true" : "clearArray: false";
                builder.AppendLine(
                    $"                global::System.Buffers.ArrayPool<{slot.ElementType}>.Shared.Return(slot_{slot.Id}, {clearArg});"
                );
                builder.AppendLine($"                slot_{slot.Id} = null;");
            }
        }

        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        foreach (PipelineSlot slot in plan.Slots)
        {
            string clearArg = slot.ClearOnReturn ? "clearArray: true" : "clearArray: false";
            builder.AppendLine(
                $"            if (slot_{slot.Id} != null) global::System.Buffers.ArrayPool<{slot.ElementType}>.Shared.Return(slot_{slot.Id}, {clearArg});"
            );
        }
        if (plan.DefFirstUse >= 0)
        {
            builder.AppendLine(
                "            if (defSlot != null) global::System.Buffers.ArrayPool<int>.Shared.Return(defSlot, clearArray: false);"
            );
        }
        builder.AppendLine(
            $"            if (rentedSource != null) global::System.Buffers.ArrayPool<{model.ClassName}>.Shared.Return(rentedSource, clearArray: {(clearSource ? "true" : "false")});"
        );
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    /// <summary>
    /// Emits one traversal of the source collection filling a single column buffer. Wrapped in a
    /// local function so the loop body stays out of the async state machine.
    /// </summary>
    private static void EmitColumnPass(
        StringBuilder builder,
        LeafColumn col,
        int slotIndex,
        bool usesParts,
        string indent
    )
    {
        builder.AppendLine($"{indent}void Extract_{slotIndex}()");
        builder.AppendLine($"{indent}{{");
        if (usesParts)
        {
            builder.AppendLine($"{indent}    int nonNull = 0;");
        }
        builder.AppendLine($"{indent}    if (arrayItems != null)");
        builder.AppendLine($"{indent}    {{");
        builder.AppendLine($"{indent}        for (int i = 0; i < count; i++)");
        builder.AppendLine($"{indent}        {{");
        builder.AppendLine($"{indent}            var item = arrayItems[i];");
        EmitColumnAssignment(builder, col, slotIndex, usesParts, indent + "            ");
        builder.AppendLine($"{indent}        }}");
        builder.AppendLine($"{indent}    }}");
        builder.AppendLine($"{indent}    else");
        builder.AppendLine($"{indent}    {{");
        builder.AppendLine($"{indent}        for (int i = 0; i < count; i++)");
        builder.AppendLine($"{indent}        {{");
        builder.AppendLine($"{indent}            var item = listItems![i];");
        EmitColumnAssignment(builder, col, slotIndex, usesParts, indent + "            ");
        builder.AppendLine($"{indent}        }}");
        builder.AppendLine($"{indent}    }}");
        if (usesParts)
        {
            builder.AppendLine($"{indent}    nonNullCount_{slotIndex} = nonNull;");
        }
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}Extract_{slotIndex}();");
    }

    private static void EmitColumnAssignment(
        StringBuilder builder,
        LeafColumn col,
        int slotIndex,
        bool usesParts,
        string indent
    )
    {
        PropertyModel prop = col.Leaf;
        if (!usesParts)
        {
            string writeExpr = PropertyMappingComponent.GetWriteExpression(
                prop,
                $"item.{prop.Name}"
            );
            builder.AppendLine($"{indent}buffer_{slotIndex}[i] = {writeExpr};");
            return;
        }

        string valVar = $"val_{slotIndex}";
        builder.AppendLine($"{indent}var {valVar} = item.{prop.Name};");
        builder.AppendLine($"{indent}if ({valVar}.HasValue)");
        builder.AppendLine($"{indent}{{");
        string nonNullExpr = prop.Kind switch
        {
            PropertyKind.Enum => $"({prop.EnumUnderlyingTypeName ?? "int"}){valVar}.Value",
            PropertyKind.TimeSpan => $"checked((int){valVar}.Value.TotalMilliseconds)",
            PropertyKind.TimeOnly => $"{valVar}.Value.Ticks / 10L",
            PropertyKind.DateOnly => $"{valVar}.Value.ToDateTime(global::System.TimeOnly.MinValue)",
            _ => $"{valVar}.Value",
        };
        builder.AppendLine($"{indent}    buffer_{slotIndex}[nonNull++] = {nonNullExpr};");
        builder.AppendLine($"{indent}    defLevels_{slotIndex}[i] = 1;");
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}else");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    defLevels_{slotIndex}[i] = 0;");
        builder.AppendLine($"{indent}}}");
    }
}
