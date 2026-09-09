using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Zero-null read fast path for nullable value-type columns (issue #150).
///
/// <para>
/// A column chunk whose footer statistic reports <c>NullCount == 0</c> carries no null holes,
/// so the densely packed page values line up one-for-one with the rows. The reader can therefore
/// skip the <c>T?[]</c> staging buffer entirely: it reads straight into a non-nullable
/// <c>T[]</c> lane with <c>ReadRawAsync</c> and lifts each value into <c>T?</c> at
/// materialization time, where the compiler emits a plain <c>Nullable&lt;T&gt;</c> constructor
/// rather than a per-row scatter pass over a wider buffer.
/// </para>
///
/// <para>
/// The definition-level buffer itself cannot be skipped: Parquet.Net 6.1.0's
/// <c>ParquetRowGroupReader.ReadRawAsync&lt;T&gt;</c> throws
/// <c>ArgumentException("Definition levels buffer is required…")</c> whenever
/// <c>MaxDefinitionLevel &gt; 0</c>, even when the caller has already proven the chunk holds no
/// nulls. The fast path therefore rents a scratch level buffer, hands it over and discards it.
/// See <c>UPSTREAM_DEPENDENCY_LIMITATIONS.md</c>.
/// </para>
/// </summary>
internal static class ZeroNullReadComponent
{
    /// <summary>
    /// True for columns that stage through a <c>T?[]</c> read buffer and can therefore take the
    /// dense lane instead. Strings and byte arrays are excluded: their null marker is the
    /// reference itself, so there is no widening to avoid.
    /// </summary>
    public static bool IsEligible(PropertyModel prop) =>
        BufferPoolComponent.UsesWriteAllParts(prop);

    public static string RawVar(int slot) => $"raw_{slot}";

    public static string FlagVar(int slot) => $"noNulls_{slot}";

    /// <summary>
    /// Emits the lazily-grown dense lane declaration that sits alongside the column's
    /// <c>T?[]</c> read buffer. Null until a chunk actually takes the fast path.
    /// </summary>
    public static void EmitDeclaration(
        StringBuilder builder,
        PropertyModel prop,
        int slot,
        string indent
    )
    {
        string denseType = BufferPoolComponent.GetNonNullableBufferType(prop);
        builder.AppendLine($"{indent}{denseType}[]? {RawVar(slot)} = null;");
    }

    /// <summary>
    /// Emits the pool return for the dense lane. Safe when the lane was never rented.
    /// </summary>
    public static void EmitReturn(
        StringBuilder builder,
        PropertyModel prop,
        int slot,
        string indent
    )
    {
        string denseType = BufferPoolComponent.GetNonNullableBufferType(prop);
        builder.AppendLine(
            $"{indent}if ({RawVar(slot)} != null) global::System.Buffers.ArrayPool<{denseType}>.Shared.Return({RawVar(slot)}, clearArray: false);"
        );
    }

    /// <summary>
    /// Emits the <c>NullCount == 0</c> branch body: grow the dense lane if needed, read the page
    /// values straight into it, then drop the definition levels Parquet.Net insists on producing.
    /// </summary>
    public static void EmitFastRead(
        StringBuilder builder,
        PropertyModel prop,
        int slot,
        string fieldAccess,
        string indent
    )
    {
        string denseType = BufferPoolComponent.GetNonNullableBufferType(prop);
        string raw = RawVar(slot);
        string scratch = $"defScratch_{slot}";

        builder.AppendLine(
            $"{indent}// Zero-null fast path (#150): the chunk statistic proves every row is present, so the"
        );
        builder.AppendLine(
            $"{indent}// dense page values map straight onto rows and the nullable staging buffer is skipped."
        );
        builder.AppendLine($"{indent}{FlagVar(slot)} = true;");
        builder.AppendLine($"{indent}if ({raw} is null || {raw}.Length < rowCount)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine(
            $"{indent}    if ({raw} != null) global::System.Buffers.ArrayPool<{denseType}>.Shared.Return({raw}, clearArray: false);"
        );
        builder.AppendLine(
            $"{indent}    {raw} = global::System.Buffers.ArrayPool<{denseType}>.Shared.Rent(rowCount);"
        );
        builder.AppendLine($"{indent}}}");
        builder.AppendLine(
            $"{indent}// Parquet.Net 6.1.0 rejects a null definition-level buffer for any field with"
        );
        builder.AppendLine(
            $"{indent}// MaxDefinitionLevel > 0, so the levels are decoded into scratch and thrown away."
        );
        builder.AppendLine(
            $"{indent}var {scratch} = global::System.Buffers.ArrayPool<int>.Shared.Rent(rowCount);"
        );
        builder.AppendLine($"{indent}try");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    await groupReader.ReadRawAsync<{denseType}>(");
        builder.AppendLine($"{indent}        {fieldAccess},");
        builder.AppendLine(
            $"{indent}        new global::System.Memory<{denseType}>({raw}, 0, rowCount),"
        );
        builder.AppendLine(
            $"{indent}        new global::System.Memory<int>({scratch}, 0, rowCount),"
        );
        builder.AppendLine($"{indent}        null,");
        builder.AppendLine($"{indent}        cancellationToken);");
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}finally");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine(
            $"{indent}    global::System.Buffers.ArrayPool<int>.Shared.Return({scratch}, clearArray: false);"
        );
        builder.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// Lifts one dense lane element into the property's nullable CLR type.
    /// </summary>
    public static string DenseReadExpression(PropertyModel prop, string valueExpression)
    {
        return prop.Kind switch
        {
            PropertyKind.Enum =>
                $"({prop.TypeName})({prop.TypeName.TrimEnd('?')}){valueExpression}",
            PropertyKind.TimeSpan =>
                $"(global::System.TimeSpan?)global::System.TimeSpan.FromMilliseconds({valueExpression})",
            PropertyKind.TimeOnly =>
                $"(global::System.TimeOnly?)new global::System.TimeOnly({valueExpression} * 10L)",
            PropertyKind.DateOnly =>
                $"(global::System.DateOnly?)global::System.DateOnly.FromDateTime({valueExpression})",
            _ => $"({prop.TypeName}){valueExpression}",
        };
    }

    /// <summary>
    /// The materialization expression for one column, selecting the dense lane when the chunk
    /// took the fast path. The selector is loop-invariant, so the branch predicts perfectly and
    /// the JIT hoists it out of the row loop's hot body.
    /// </summary>
    public static string SelectReadExpression(
        PropertyModel prop,
        int slot,
        string indexVar,
        string bufferPrefix,
        bool zeroNullFastPath
    )
    {
        string standard = PropertyMappingComponent.GetReadExpression(
            prop,
            $"{bufferPrefix}{slot}[{indexVar}]"
        );

        if (!zeroNullFastPath || !IsEligible(prop))
        {
            return standard;
        }

        string dense = DenseReadExpression(prop, $"{RawVar(slot)}![{indexVar}]");
        return $"{FlagVar(slot)} ? {dense} : ({standard})";
    }
}
