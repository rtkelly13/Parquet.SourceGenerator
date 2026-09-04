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
        string indent = "        "
    )
    {
        if (declareRowGroupCount)
        {
            builder.AppendLine($"{indent}int {rowGroupCountVar} = {readerVar}.RowGroupCount;");
        }
        builder.AppendLine($"{indent}int {rowCountVar} = 0;");
        builder.AppendLine($"{indent}int {maxRowVar} = 0;");
        builder.AppendLine($"{indent}for (int r = 0; r < {rowGroupCountVar}; r++)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    int rc = (int){readerVar}.RowGroups[r].RowCount;");
        builder.AppendLine($"{indent}    {rowCountVar} += rc;");
        builder.AppendLine($"{indent}    if (rc > {maxRowVar}) {maxRowVar} = rc;");
        builder.AppendLine($"{indent}}}");
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
        string indent = "        "
    )
    {
        if (declareVariables)
        {
            builder.AppendLine($"{indent}int {rowGroupCountVar} = {readerVar}.RowGroupCount;");
            builder.AppendLine($"{indent}int {rowCountVar} = 0;");
            builder.AppendLine($"{indent}int {maxRowVar} = 0;");
            builder.AppendLine($"{indent}var {offsetsVar} = new int[{rowGroupCountVar}];");
        }
        else
        {
            builder.AppendLine($"{indent}{rowGroupCountVar} = {readerVar}.RowGroupCount;");
            builder.AppendLine($"{indent}{rowCountVar} = 0;");
            builder.AppendLine($"{indent}{maxRowVar} = 0;");
            builder.AppendLine($"{indent}{offsetsVar} = new int[{rowGroupCountVar}];");
        }
        builder.AppendLine($"{indent}for (int r = 0; r < {rowGroupCountVar}; r++)");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    int rc = (int){readerVar}.RowGroups[r].RowCount;");
        builder.AppendLine($"{indent}    {offsetsVar}[r] = {rowCountVar};");
        builder.AppendLine($"{indent}    {rowCountVar} += rc;");
        builder.AppendLine($"{indent}    if (rc > {maxRowVar}) {maxRowVar} = rc;");
        builder.AppendLine($"{indent}}}");
    }
}
