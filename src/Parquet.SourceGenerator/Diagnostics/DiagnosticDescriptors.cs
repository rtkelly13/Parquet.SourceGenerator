using Microsoft.CodeAnalysis;

namespace Parquet.SourceGenerator.Diagnostics;

/// <summary>
/// Roslyn compiler diagnostic descriptors emitted by Parquet.SourceGenerator.
/// </summary>
public static class DiagnosticDescriptors
{
    /// <summary>
    /// Emitted when a type decorated with [ParquetSerializable] does not have an accessible parameterless constructor or primary record constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingAccessibleConstructor = new(
        id: "PARQ001",
        title: "Missing accessible parameterless constructor",
        messageFormat: "Type '{0}' decorated with [ParquetSerializable] must have an accessible parameterless constructor or primary constructor",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Emitted when a property type is not supported for automatic Parquet schema generation.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        id: "PARQ002",
        title: "Unsupported property type",
        messageFormat: "Property '{0}' on type '{1}' has unsupported type '{2}' for Parquet serialization",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Emitted when duplicate column names exist within the same target schema.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateColumnName = new(
        id: "PARQ003",
        title: "Duplicate column name in schema",
        messageFormat: "Type '{0}' contains multiple properties bound to Parquet column name '{1}'",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Emitted when ParquetDecimalAttribute scale is greater than precision.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidDecimalScale = new(
        id: "PARQ004",
        title: "Invalid decimal precision/scale configuration",
        messageFormat: "Property '{0}' on type '{1}' has invalid decimal configuration: precision ({2}) must be greater than scale ({3})",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
