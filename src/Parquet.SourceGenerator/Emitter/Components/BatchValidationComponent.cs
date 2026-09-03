using System.Text;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Composable emitter component for row group batch size resolution and argument validation.
/// </summary>
internal static class BatchValidationComponent
{
    /// <summary>
    /// Emits runtime resolution of the row group batch size from explicit parameter and options.
    /// </summary>
    public static void EmitRowGroupSizeResolution(StringBuilder builder, string targetVar = "targetChunkSize")
    {
        builder.AppendLine("        if (rowGroupSize.HasValue && rowGroupSize.Value <= 0)");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(rowGroupSize));");
        builder.AppendLine();
        builder.AppendLine("        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;");
        builder.AppendLine($"        int {targetVar} = rowGroupSize ?? options.RowGroupSize;");
        builder.AppendLine($"        if ({targetVar} <= 0)");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(options), \"ParquetSerializerOptions.RowGroupSize must be greater than zero.\");");
    }
}
