using System;
using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter;

public static partial class CodeEmitter
{
    // ──────────────────────────────────────────────────────────
    //  HELPERS
    // ──────────────────────────────────────────────────────────

    private static void EmitPropertyAssignments(StringBuilder builder, TargetClassModel model, string bufPrefix, string prefix)
    {
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string writeExpr = GetWriteExpression(prop, $"item.{prop.Name}");
            builder.AppendLine($"{prefix}{bufPrefix}{i}[i] = {writeExpr};");
        }
    }

    private static void EmitPropertyAssignmentsIndexed(StringBuilder builder, TargetClassModel model, string bufPrefix, string prefix)
    {
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string writeExpr = GetWriteExpression(prop, $"item.{prop.Name}");
            builder.AppendLine($"{prefix}{bufPrefix}{i}[idx] = {writeExpr};");
        }
    }

    private static string GetBufferElementType(PropertyModel prop)
    {
        return prop.Kind switch
        {
            PropertyKind.Guid when prop.IsNullable => "global::System.Guid?",
            PropertyKind.Guid => "global::System.Guid",
            PropertyKind.Enum when prop.IsNullable => $"{prop.EnumUnderlyingTypeName ?? "int"}?",
            PropertyKind.Enum => prop.EnumUnderlyingTypeName ?? "int",
            _ => prop.TypeName,
        };
    }

    private static bool IsReferenceTypeBuffer(PropertyModel prop)
    {
        return prop.Kind switch
        {
            PropertyKind.Guid => false, // Guid is a 16-byte value type struct!
            PropertyKind.ByteArray => true,
            PropertyKind.Primitive when prop.TypeName.Contains("string") => true,
            _ => false,
        };
    }

    private static string GetWriteExpression(PropertyModel prop, string valueExpr) =>
        EmitterShared.GetWriteExpression(prop, valueExpr);

    private static string GetReadExpression(PropertyModel prop, string valueExpr) =>
        EmitterShared.GetReadExpression(prop, valueExpr);

    private static string GetWritePrimitiveCall(PropertyModel prop, string fieldAccess, string bufName)
    {
        bool isString = prop.Kind == PropertyKind.Primitive && prop.TypeName.Contains("string");
        bool isByteArray = prop.Kind == PropertyKind.ByteArray;

        if (isString)
        {
            return $"await groupWriter.WriteAsync({fieldAccess}, new global::System.ArraySegment<string?>({bufName}, 0, count));";
        }
        else if (isByteArray)
        {
            return $"await groupWriter.WriteAsync({fieldAccess}, new global::System.ArraySegment<byte[]?>({bufName}, 0, count));";
        }
        else if (prop.Kind == PropertyKind.Guid)
        {
            if (prop.IsNullable)
                return $"await groupWriter.WriteAsync<global::System.Guid>({fieldAccess}, new global::System.ReadOnlyMemory<global::System.Guid?>({bufName}, 0, count), cancellationToken: cancellationToken);";
            else
                return $"await groupWriter.WriteAsync<global::System.Guid>({fieldAccess}, new global::System.ReadOnlyMemory<global::System.Guid>({bufName}, 0, count), cancellationToken: cancellationToken);";
        }
        else if (prop.Kind == PropertyKind.Enum)
        {
            string underlying = prop.EnumUnderlyingTypeName ?? "int";
            if (prop.IsNullable)
                return $"await groupWriter.WriteAsync<{underlying}>({fieldAccess}, new global::System.ReadOnlyMemory<{underlying}?>({bufName}, 0, count), cancellationToken: cancellationToken);";
            else
                return $"await groupWriter.WriteAsync<{underlying}>({fieldAccess}, new global::System.ReadOnlyMemory<{underlying}>({bufName}, 0, count), cancellationToken: cancellationToken);";
        }
        else
        {
            string structType = prop.TypeName.TrimEnd('?');
            if (prop.IsNullable)
                return $"await groupWriter.WriteAsync<{structType}>({fieldAccess}, new global::System.ReadOnlyMemory<{structType}?>({bufName}, 0, count), cancellationToken: cancellationToken);";
            else
                return $"await groupWriter.WriteAsync<{structType}>({fieldAccess}, new global::System.ReadOnlyMemory<{structType}>({bufName}, 0, count), cancellationToken: cancellationToken);";
        }
    }

    private static string GetReadPrimitiveCall(PropertyModel prop, string fieldAccess, string bufName)
    {
        bool isString = prop.Kind == PropertyKind.Primitive && prop.TypeName.Contains("string");
        bool isByteArray = prop.Kind == PropertyKind.ByteArray;

        if (isString)
        {
            return $"await groupReader.ReadAsync({fieldAccess}, new global::System.Memory<string?>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
        }
        else if (isByteArray)
        {
            return $"await groupReader.ReadAsync({fieldAccess}, new global::System.Memory<byte[]?>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
        }
        else if (prop.Kind == PropertyKind.Guid)
        {
            if (prop.IsNullable)
                return $"await groupReader.ReadAsync<global::System.Guid>({fieldAccess}, new global::System.Memory<global::System.Guid?>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
            else
                return $"await groupReader.ReadAsync<global::System.Guid>({fieldAccess}, new global::System.Memory<global::System.Guid>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
        }
        else if (prop.Kind == PropertyKind.Enum)
        {
            string underlying = prop.EnumUnderlyingTypeName ?? "int";
            if (prop.IsNullable)
                return $"await groupReader.ReadAsync<{underlying}>({fieldAccess}, new global::System.Memory<{underlying}?>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
            else
                return $"await groupReader.ReadAsync<{underlying}>({fieldAccess}, new global::System.Memory<{underlying}>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
        }
        else
        {
            string structType = prop.TypeName.TrimEnd('?');
            if (prop.IsNullable)
                return $"await groupReader.ReadAsync<{structType}>({fieldAccess}, new global::System.Memory<{structType}?>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
            else
                return $"await groupReader.ReadAsync<{structType}>({fieldAccess}, new global::System.Memory<{structType}>({bufName}, 0, rowCount), cancellationToken: cancellationToken);";
        }
    }


    private static void EmitBuildFormatOptions(StringBuilder builder)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Translates the generator's options into the Parquet.Net options the writer accepts.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    private static global::Parquet.ParquetOptions BuildFormatOptions(");
        builder.AppendLine("        global::Parquet.SourceGenerator.ParquetSerializerOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        var formatOptions = new global::Parquet.ParquetOptions");
        builder.AppendLine("        {");
        builder.AppendLine("            CompressionMethod = options.CompressionMethod switch");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionMethod.None =>");
        builder.AppendLine("                    global::Parquet.CompressionMethod.None,");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionMethod.Gzip =>");
        builder.AppendLine("                    global::Parquet.CompressionMethod.Gzip,");
        builder.AppendLine("                // Parquet.Net spells this one in caps.");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionMethod.Lz4 =>");
        builder.AppendLine("                    global::Parquet.CompressionMethod.LZ4,");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionMethod.Brotli =>");
        builder.AppendLine("                    global::Parquet.CompressionMethod.Brotli,");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionMethod.Zstd =>");
        builder.AppendLine("                    global::Parquet.CompressionMethod.Zstd,");
        builder.AppendLine("                _ => global::Parquet.CompressionMethod.Snappy,");
        builder.AppendLine("            },");
        builder.AppendLine("        };");
        builder.AppendLine();
        // Assigned only when the caller asked for one, so "unspecified" keeps Parquet.Net's own
        // default (SmallestSize) rather than this generator silently picking a level for them.
        builder.AppendLine("        if (options.CompressionLevel.HasValue)");
        builder.AppendLine("        {");
        builder.AppendLine("            formatOptions.CompressionLevel = options.CompressionLevel.Value switch");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionLevel.Optimal =>");
        builder.AppendLine("                    global::System.IO.Compression.CompressionLevel.Optimal,");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionLevel.Fastest =>");
        builder.AppendLine("                    global::System.IO.Compression.CompressionLevel.Fastest,");
        builder.AppendLine("                global::Parquet.SourceGenerator.ParquetCompressionLevel.NoCompression =>");
        builder.AppendLine("                    global::System.IO.Compression.CompressionLevel.NoCompression,");
        builder.AppendLine("                _ => global::System.IO.Compression.CompressionLevel.SmallestSize,");
        builder.AppendLine("            };");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return formatOptions;");
        builder.AppendLine("    }");
    }

    private static string BoolLiteral(bool value) => EmitterShared.BoolLiteral(value);
}
