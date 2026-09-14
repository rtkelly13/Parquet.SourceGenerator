using System.Text;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Composable emitter component for upfront metadata layout calculation (total row count, row offsets, max row group size).
/// </summary>
internal static class RowGroupLayoutComponent
{
    /// <summary>
    /// Emits a single-pass loop over row groups to gather total count and max group size.
    /// </summary>
    public static void EmitLayoutProbe(
        StringBuilder builder,
        string readerVar = "reader",
        string rowCountVar = "totalRows",
        string maxRowVar = "maxRowCount",
        string rowGroupCountVar = "rowGroupCount",
        bool declareRowGroupCount = true,
        string indent = "        ",
        string optionsVar = "options"
    )
    {
        if (declareRowGroupCount)
        {
            builder.AppendLine($"{indent}int {rowGroupCountVar} = {readerVar}.RowGroupCount;");
        }
        builder.AppendLine($"{indent}if ({rowGroupCountVar} < 0 || {rowGroupCountVar} > {optionsVar}.MaxRowGroupCount)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    throw new global::System.IO.InvalidDataException($\"Row group count {{{rowGroupCountVar}}} is invalid or exceeds maximum allowed {{{optionsVar}.MaxRowGroupCount}}.\");");
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}long {rowCountVar}Long = 0;");
        builder.AppendLine($"{indent}int {maxRowVar} = 0;");
        builder.AppendLine($"{indent}for (int r = 0; r < {rowGroupCountVar}; r++)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    long rcLong = {readerVar}.RowGroups[r].RowCount;");
        builder.AppendLine($"{indent}    if (rcLong < 0 || rcLong > {optionsVar}.MaxAllocationValues)");
        builder.AppendLine($"{indent}    {{");
        builder.AppendLine($"{indent}        throw new global::System.IO.InvalidDataException($\"Row group {{r}} row count {{rcLong}} is invalid or exceeds maximum allowed {{{optionsVar}.MaxAllocationValues}}.\");");
        builder.AppendLine($"{indent}    }}");
        builder.AppendLine($"{indent}    {rowCountVar}Long = checked({rowCountVar}Long + rcLong);");
        builder.AppendLine($"{indent}    int rc = (int)rcLong;");
        builder.AppendLine($"{indent}    if (rc > {maxRowVar}) {maxRowVar} = rc;");
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}if ({rowCountVar}Long > {optionsVar}.MaxAllocationValues)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    throw new global::System.IO.InvalidDataException($\"Total row count {{{rowCountVar}Long}} exceeds maximum allowed {{{optionsVar}.MaxAllocationValues}}.\");");
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}int {rowCountVar} = checked((int){rowCountVar}Long);");
    }

    /// <summary>
    /// Emits layout probe that also records pre-indexed rowOffsets array for direct parallel/indexed population.
    /// </summary>
    public static void EmitIndexedLayoutProbe(
        StringBuilder builder,
        string readerVar = "reader",
        string rowGroupCountVar = "rowGroupCount",
        string offsetsVar = "rowOffsets",
        string rowCountVar = "totalRows",
        string maxRowVar = "maxRowGroupSize",
        bool declareVariables = true,
        string indent = "        ",
        string optionsVar = "options"
    )
    {
        if (declareVariables)
        {
            builder.AppendLine($"{indent}int {rowGroupCountVar} = {readerVar}.RowGroupCount;");
            builder.AppendLine($"{indent}if ({rowGroupCountVar} < 0 || {rowGroupCountVar} > {optionsVar}.MaxRowGroupCount)");
            builder.AppendLine($"{indent}{{");
            builder.AppendLine($"{indent}    throw new global::System.IO.InvalidDataException($\"Row group count {{{rowGroupCountVar}}} is invalid or exceeds maximum allowed {{{optionsVar}.MaxRowGroupCount}}.\");");
            builder.AppendLine($"{indent}}}");
            builder.AppendLine($"{indent}long {rowCountVar}Long = 0;");
            builder.AppendLine($"{indent}int {maxRowVar} = 0;");
            builder.AppendLine($"{indent}var {offsetsVar} = new int[{rowGroupCountVar}];");
        }
        else
        {
            builder.AppendLine($"{indent}{rowGroupCountVar} = {readerVar}.RowGroupCount;");
            builder.AppendLine($"{indent}if ({rowGroupCountVar} < 0 || {rowGroupCountVar} > {optionsVar}.MaxRowGroupCount)");
            builder.AppendLine($"{indent}{{");
            builder.AppendLine($"{indent}    throw new global::System.IO.InvalidDataException($\"Row group count {{{rowGroupCountVar}}} is invalid or exceeds maximum allowed {{{optionsVar}.MaxRowGroupCount}}.\");");
            builder.AppendLine($"{indent}}}");
            builder.AppendLine($"{indent}long {rowCountVar}Long = 0;");
            builder.AppendLine($"{indent}{maxRowVar} = 0;");
            builder.AppendLine($"{indent}{offsetsVar} = new int[{rowGroupCountVar}];");
        }
        builder.AppendLine($"{indent}for (int r = 0; r < {rowGroupCountVar}; r++)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    long rcLong = {readerVar}.RowGroups[r].RowCount;");
        builder.AppendLine($"{indent}    if (rcLong < 0 || rcLong > {optionsVar}.MaxAllocationValues)");
        builder.AppendLine($"{indent}    {{");
        builder.AppendLine($"{indent}        throw new global::System.IO.InvalidDataException($\"Row group {{r}} row count {{rcLong}} is invalid or exceeds maximum allowed {{{optionsVar}.MaxAllocationValues}}.\");");
        builder.AppendLine($"{indent}    }}");
        builder.AppendLine($"{indent}    {offsetsVar}[r] = checked((int){rowCountVar}Long);");
        builder.AppendLine($"{indent}    {rowCountVar}Long = checked({rowCountVar}Long + rcLong);");
        builder.AppendLine($"{indent}    int rc = (int)rcLong;");
        builder.AppendLine($"{indent}    if (rc > {maxRowVar}) {maxRowVar} = rc;");
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}if ({rowCountVar}Long > {optionsVar}.MaxAllocationValues)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    throw new global::System.IO.InvalidDataException($\"Total row count {{{rowCountVar}Long}} exceeds maximum allowed {{{optionsVar}.MaxAllocationValues}}.\");");
        builder.AppendLine($"{indent}}}");
        if (declareVariables)
        {
            builder.AppendLine($"{indent}int {rowCountVar} = checked((int){rowCountVar}Long);");
        }
        else
        {
            builder.AppendLine($"{indent}{rowCountVar} = checked((int){rowCountVar}Long);");
        }
    }
}
