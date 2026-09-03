using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Composable emitter component for property extraction (writing) and object materialization (reading).
/// </summary>
internal static class PropertyMappingComponent
{
    /// <summary>
    /// The expression that converts a model member into its column/buffer representation (handling enum conversions).
    /// </summary>
    public static string GetWriteExpression(PropertyModel prop, string valueExpression)
    {
        if (prop.Kind != PropertyKind.Enum)
            return valueExpression;

        string underlying = prop.EnumUnderlyingTypeName ?? "int";
        return prop.IsNullable
            ? $"{valueExpression} is null ? ({underlying}?)null : ({underlying}){valueExpression}.Value"
            : $"({underlying}){valueExpression}";
    }

    /// <summary>
    /// The expression that converts a column/buffer value back into the model member's type (handling enum conversions).
    /// </summary>
    public static string GetReadExpression(PropertyModel prop, string valueExpression)
    {
        if (prop.Kind != PropertyKind.Enum)
            return valueExpression;

        return prop.IsNullable
            ? $"{valueExpression} is null ? ({prop.TypeName})null : ({prop.TypeName.TrimEnd('?')}){valueExpression}!"
            : $"({prop.TypeName.TrimEnd('?')}){valueExpression}";
    }

    /// <summary>
    /// Emits object materialization instantiation:
    /// target[targetOffset + index] = new T { Prop1 = ..., Prop2 = ... };
    /// </summary>
    public static void EmitObjectMaterialization(
        StringBuilder builder,
        TargetClassModel model,
        string targetArray,
        string offsetExpr,
        string indexVar,
        string bufferPrefix = "buffer_",
        string indent = "                ")
    {
        string targetSlot = string.IsNullOrEmpty(offsetExpr)
            ? $"{targetArray}[{indexVar}]"
            : $"{targetArray}[{offsetExpr} + {indexVar}]";

        builder.AppendLine($"{indent}{targetSlot} = new {model.ClassName}");
        builder.AppendLine($"{indent}{{");

        for (int i = 0; i < model.Properties.Length; i++)
        {
            PropertyModel prop = model.Properties[i];
            string readExpr = GetReadExpression(prop, $"{bufferPrefix}{i}[{indexVar}]");
            builder.AppendLine($"{indent}    {prop.Name} = {readExpr},");
        }

        builder.AppendLine($"{indent}}};");
    }

    /// <summary>
    /// Emits property extraction from an item into column buffers:
    /// buffer_{i}[index] = item.Prop;
    /// </summary>
    public static void EmitPropertyAssignments(
        StringBuilder builder,
        TargetClassModel model,
        string itemVar = "item",
        string indexVar = "i",
        string bufferPrefix = "buffer_",
        string indent = "                    ")
    {
        for (int p = 0; p < model.Properties.Length; p++)
        {
            PropertyModel prop = model.Properties[p];
            string writeExpr = GetWriteExpression(prop, $"{itemVar}.{prop.Name}");
            builder.AppendLine($"{indent}{bufferPrefix}{p}[{indexVar}] = {writeExpr};");
        }
    }
}
