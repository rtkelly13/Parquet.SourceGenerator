using System;
using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter;

/// <summary>
/// Shared code emission utilities reused across both v6 (<c>CodeEmitter</c>)
/// and v4/v5 (<c>LegacyCodeEmitter</c>) source generators.
/// </summary>
internal static class EmitterShared
{
    /// <summary>
    /// Formats a boolean as a C# literal ("true" or "false").
    /// </summary>
    public static string BoolLiteral(bool value) => value ? "true" : "false";

    /// <summary>
    /// Generates compile-time ParquetSchema DataField instantiation code.
    /// </summary>
    public static string GetFieldCreationExpression(PropertyModel prop)
    {
        // The column name comes from user source — `[ParquetColumn("... ")]` — so it can contain
        // anything a C# string literal can, including quotes and backslashes. FormatLiteral
        // emits the surrounding quotes and escapes the contents.
        string name = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(prop.ParquetColumnName, quote: true);

        return prop.Kind switch
        {
            PropertyKind.Decimal when prop.DecimalPrecision.HasValue && prop.DecimalScale.HasValue =>
                $"new global::Parquet.Schema.DecimalDataField({name}, {prop.DecimalPrecision.Value}, {prop.DecimalScale.Value}, isNullable: {BoolLiteral(prop.IsNullable)})",

            PropertyKind.Decimal =>
                $"new global::Parquet.Schema.DecimalDataField({name}, 38, 18, isNullable: {BoolLiteral(prop.IsNullable)})",

            PropertyKind.DateTime when prop.TimestampUnit == "1" || prop.TimestampUnit?.Contains("Microseconds") == true =>
                $"new global::Parquet.Schema.DateTimeDataField({name}, global::Parquet.Schema.DateTimeFormat.DateAndTimeMicros, isNullable: {BoolLiteral(prop.IsNullable)})",

            PropertyKind.DateTime =>
                $"new global::Parquet.Schema.DateTimeDataField({name}, global::Parquet.Schema.DateTimeFormat.Impala, isNullable: {BoolLiteral(prop.IsNullable)})",

            PropertyKind.TimeSpan =>
                $"new global::Parquet.Schema.TimeSpanDataField({name}, global::Parquet.Schema.TimeSpanFormat.MilliSeconds, isNullable: {BoolLiteral(prop.IsNullable)})",

            PropertyKind.Guid =>
                $"new global::Parquet.Schema.DataField({name}, typeof(global::System.Guid), isNullable: {BoolLiteral(prop.IsNullable)})",

            PropertyKind.Enum =>
                $"new global::Parquet.Schema.DataField({name}, typeof({prop.EnumUnderlyingTypeName ?? "int"}), isNullable: {BoolLiteral(prop.IsNullable)})",

            PropertyKind.ByteArray =>
                $"new global::Parquet.Schema.DataField({name}, typeof(byte[]), isNullable: {BoolLiteral(prop.IsNullable)})",

            _ =>
                $"new global::Parquet.Schema.DataField({name}, typeof({prop.TypeName.TrimEnd('?')}), isNullable: {BoolLiteral(prop.IsNullable)})",
        };
    }

    /// <summary>
    /// Emits static cached DataField references from the static Schema.
    /// </summary>
    public static void EmitStaticFields(StringBuilder builder, TargetClassModel model)
    {
        for (int i = 0; i < model.Properties.Length; i++)
        {
            builder.AppendLine($"    private static readonly global::Parquet.Schema.DataField _field_{i} = (global::Parquet.Schema.DataField)Schema.Fields[{i}];");
        }
    }

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
}
