using System;
using System.Text;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Legacy.Emitter;

public static partial class LegacyCodeEmitter
{
    private static void EmitBuildFormatOptions(StringBuilder builder)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Translates the generator's options into the Parquet.Net options the reader and writer accept.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    /// <remarks>");
        builder.AppendLine("    /// Parquet.Net v4/v5 keeps compression on <c>ParquetWriter</c> rather than on");
        builder.AppendLine("    /// <c>ParquetOptions</c> — see <c>ApplyCompression</c>. <c>ParquetOptions</c> itself carries only");
        builder.AppendLine("    /// decoding hints (<c>TreatByteArrayAsString</c> and friends) that this generator does not");
        builder.AppendLine("    /// currently expose, so the returned instance is deliberately left at its defaults.");
        builder.AppendLine("    /// </remarks>");
        builder.AppendLine("    private static global::Parquet.ParquetOptions BuildFormatOptions(");
        builder.AppendLine("        global::Parquet.SourceGenerator.ParquetSerializerOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        return new global::Parquet.ParquetOptions();");
        builder.AppendLine("    }");
    }

    private static void EmitApplyCompression(StringBuilder builder)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Applies the requested compression settings to a v4/v5 writer.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    /// <remarks>");
        builder.AppendLine("    /// <c>CompressionMethod</c> is a property and <c>CompressionLevel</c> a field on");
        builder.AppendLine("    /// <c>ParquetWriter</c> in this API generation; both must be set after the writer exists and");
        builder.AppendLine("    /// before the first row group is written. Without this, every option was silently discarded and");
        builder.AppendLine("    /// a Gzip request wrote Snappy.");
        builder.AppendLine("    /// </remarks>");
        builder.AppendLine("    private static void ApplyCompression(");
        builder.AppendLine("        global::Parquet.ParquetWriter writer,");
        builder.AppendLine("        global::Parquet.SourceGenerator.ParquetSerializerOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        writer.CompressionMethod = options.CompressionMethod switch");
        builder.AppendLine("        {");
        builder.AppendLine("            global::Parquet.SourceGenerator.ParquetCompressionMethod.None =>");
        builder.AppendLine("                global::Parquet.CompressionMethod.None,");
        builder.AppendLine("            global::Parquet.SourceGenerator.ParquetCompressionMethod.Gzip =>");
        builder.AppendLine("                global::Parquet.CompressionMethod.Gzip,");
        builder.AppendLine("            // Parquet.Net spells this one in caps.");
        builder.AppendLine("            global::Parquet.SourceGenerator.ParquetCompressionMethod.Lz4 =>");
        builder.AppendLine("                global::Parquet.CompressionMethod.LZ4,");
        builder.AppendLine("            global::Parquet.SourceGenerator.ParquetCompressionMethod.Brotli =>");
        builder.AppendLine("                global::Parquet.CompressionMethod.Brotli,");
        builder.AppendLine("            global::Parquet.SourceGenerator.ParquetCompressionMethod.Zstd =>");
        builder.AppendLine("                global::Parquet.CompressionMethod.Zstd,");
        builder.AppendLine("            _ => global::Parquet.CompressionMethod.Snappy,");
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("        if (!options.CompressionLevel.HasValue) return;");
        builder.AppendLine();
        builder.AppendLine("        switch (options.CompressionLevel.Value)");
        builder.AppendLine("        {");
        builder.AppendLine("            case global::Parquet.SourceGenerator.ParquetCompressionLevel.Fastest:");
        builder.AppendLine("                writer.CompressionLevel = global::System.IO.Compression.CompressionLevel.Fastest;");
        builder.AppendLine("                break;");
        builder.AppendLine("            case global::Parquet.SourceGenerator.ParquetCompressionLevel.NoCompression:");
        builder.AppendLine("                writer.CompressionLevel = global::System.IO.Compression.CompressionLevel.NoCompression;");
        builder.AppendLine("                break;");
        // CompressionLevel.SmallestSize arrived in .NET 6. This generated code compiles inside the
        // consumer's project, so on net472 / netstandard2.0 — the targets this backend exists for —
        // naming it would not compile. Optimal is the strongest level those targets have.
        builder.AppendLine("#if NET6_0_OR_GREATER");
        builder.AppendLine("            case global::Parquet.SourceGenerator.ParquetCompressionLevel.SmallestSize:");
        builder.AppendLine("                writer.CompressionLevel = global::System.IO.Compression.CompressionLevel.SmallestSize;");
        builder.AppendLine("                break;");
        builder.AppendLine("#endif");
        builder.AppendLine("            default:");
        builder.AppendLine("                // Includes SmallestSize below .NET 6, where Optimal is the strongest level available.");
        builder.AppendLine("                writer.CompressionLevel = global::System.IO.Compression.CompressionLevel.Optimal;");
        builder.AppendLine("                break;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    // ──────────────────────────────────────────────────────────

    //  TYPE MAPPING
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// The CLR element type of the array a <c>DataColumn</c> carries for this member.
    /// </summary>
    /// <remarks>
    /// <c>DataColumn</c>'s validation is exact: it compares the array's element type against
    /// <c>DataField.ClrNullableIfHasNullsType</c> and throws otherwise. So a nullable value-type
    /// column needs <c>T?[]</c>, and a reference-type column needs the *unannotated* type — the
    /// nullable-reference annotation carries no runtime meaning and there is no <c>Nullable&lt;string&gt;</c>.
    /// </remarks>
    private static string GetColumnElementType(PropertyModel prop)
    {
        string enumUnderlying = prop.EnumUnderlyingTypeName ?? "int";

        return prop.Kind switch
        {
            PropertyKind.ByteArray => "byte[]",
            PropertyKind.Enum => prop.IsNullable ? enumUnderlying + "?" : enumUnderlying,
            PropertyKind.Guid => prop.IsNullable ? "global::System.Guid?" : "global::System.Guid",
            _ => IsReferenceTypeColumn(prop) ? prop.TypeName.TrimEnd('?') : prop.TypeName,
        };
    }

    /// <summary>
    /// Builds the <c>new T[count]</c> expression for a column, placing array ranks after the length.
    /// </summary>
    private static string GetArrayCreationExpression(PropertyModel prop, string countExpression)
    {
        string element = GetColumnElementType(prop);

        int bracket = element.IndexOf('[');
        if (bracket >= 0)
        {
            // `byte[]` becomes `new byte[count][]`, not `new byte[][count]`.
            return $"new {element.Substring(0, bracket)}[{countExpression}]{element.Substring(bracket)}";
        }

        return $"new {element}[{countExpression}]";
    }

    private static bool IsReferenceTypeColumn(PropertyModel prop)
    {
        return prop.Kind switch
        {
            PropertyKind.ByteArray => true,
            // Guid is a 16-byte struct, so it takes the value-type path despite the size.
            PropertyKind.Guid => false,
            PropertyKind.Primitive => prop.TypeName.Contains("string"),
            _ => false,
        };
    }

    private static string GetWriteExpression(PropertyModel prop, string valueExpression) =>
        EmitterShared.GetWriteExpression(prop, valueExpression);

    private static string GetReadExpression(PropertyModel prop, string valueExpression) =>
        EmitterShared.GetReadExpression(prop, valueExpression);

    private static string BoolLiteral(bool val) => EmitterShared.BoolLiteral(val);
}
