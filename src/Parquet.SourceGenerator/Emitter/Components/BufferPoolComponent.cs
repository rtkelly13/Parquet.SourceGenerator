using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Composable emitter component for ArrayPool column buffer rental and return lifecycles.
/// </summary>
internal static class BufferPoolComponent
{
    public static string GetBufferElementType(PropertyModel prop)
    {
        return prop.Kind switch
        {
            PropertyKind.Guid when prop.IsNullable => "global::System.Guid?",
            PropertyKind.Guid => "global::System.Guid",
            PropertyKind.Enum when prop.IsNullable => $"{prop.EnumUnderlyingTypeName ?? "int"}?",
            PropertyKind.Enum => prop.EnumUnderlyingTypeName ?? "int",
            PropertyKind.TimeSpan when prop.IsNullable => "int?",
            PropertyKind.TimeSpan => "int",
            PropertyKind.TimeOnly when prop.IsNullable => "long?",
            PropertyKind.TimeOnly => "long",
            _ => prop.TypeName,
        };
    }

    public static string GetWriteBufferElementType(PropertyModel prop)
    {
        if (prop.Kind == PropertyKind.Primitive && prop.TypeName.Contains("string"))
        {
            return prop.IsNullable
                ? "global::System.ReadOnlyMemory<char>?"
                : "global::System.ReadOnlyMemory<char>";
        }

        if (prop.Kind == PropertyKind.ByteArray)
        {
            return prop.IsNullable
                ? "global::System.ReadOnlyMemory<byte>?"
                : "global::System.ReadOnlyMemory<byte>";
        }

        return GetBufferElementType(prop);
    }

    public static bool IsReferenceTypeBuffer(PropertyModel prop, bool isWrite = false)
    {
        if (isWrite)
        {
            if (
                prop.Kind == PropertyKind.ByteArray
                || (prop.Kind == PropertyKind.Primitive && prop.TypeName.Contains("string"))
            )
            {
                return true;
            }
        }

        return prop.Kind switch
        {
            PropertyKind.Guid => false,
            PropertyKind.ByteArray => true,
            PropertyKind.Primitive when prop.TypeName.Contains("string") => true,
            _ => false,
        };
    }

    /// <summary>
    /// Emits ArrayPool rentals for all property column buffers.
    /// </summary>
    public static void EmitRentals(
        StringBuilder builder,
        TargetClassModel model,
        string sizeExpr,
        string varPrefix = "buffer_",
        string indent = "        ",
        bool isWrite = false
    )
    {
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = isWrite ? GetWriteBufferElementType(prop) : GetBufferElementType(prop);
            builder.AppendLine(
                $"{indent}var {varPrefix}{i} = global::System.Buffers.ArrayPool<{bufType}>.Shared.Rent({sizeExpr});"
            );
        }
    }

    /// <summary>
    /// Emits ArrayPool returns for all property column buffers.
    /// </summary>
    public static void EmitReturns(
        StringBuilder builder,
        TargetClassModel model,
        string varPrefix = "buffer_",
        string indent = "            ",
        bool isWrite = false
    )
    {
        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string bufType = isWrite ? GetWriteBufferElementType(prop) : GetBufferElementType(prop);
            bool isRef = IsReferenceTypeBuffer(prop, isWrite);
            string clearArg = isRef ? "clearArray: true" : "clearArray: false";
            builder.AppendLine(
                $"{indent}global::System.Buffers.ArrayPool<{bufType}>.Shared.Return({varPrefix}{i}, {clearArg});"
            );
        }
    }
}
