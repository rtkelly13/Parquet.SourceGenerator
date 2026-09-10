using System.Text;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Composable emitter component for row group batch size resolution and argument validation.
/// </summary>
internal static class BatchValidationComponent
{
    /// <summary>
    /// Emits runtime resolution of the row group batch size from <c>ParquetSerializerOptions</c>,
    /// the single home for that setting.
    /// </summary>
    public static void EmitRowGroupSizeResolution(
        StringBuilder builder,
        string targetVar = "targetChunkSize"
    )
    {
        builder.AppendLine(
            "        options ??= global::Parquet.SourceGenerator.ParquetSerializerOptions.Default;"
        );
        builder.AppendLine($"        int {targetVar} = options.RowGroupSize;");
        builder.AppendLine($"        if ({targetVar} <= 0)");
        builder.AppendLine(
            "            throw new global::System.ArgumentOutOfRangeException(nameof(options), \"ParquetSerializerOptions.RowGroupSize must be greater than zero.\");"
        );
    }
}
