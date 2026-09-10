using System.Text;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Composable emitter component for row group batch size resolution and argument validation.
/// </summary>
internal static class BatchValidationComponent
{
    /// <summary>
    /// Emits runtime resolution of the row group batch size from options.
    /// <para>
    /// Issue #218: this used to reconcile a <c>rowGroupSize</c> parameter against
    /// <c>ParquetSerializerOptions.RowGroupSize</c>, so the same
    /// knob had two homes and a caller who set both could not tell from the signature which won.
    /// Options is now the single home.
    /// </para>
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
