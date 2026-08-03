using Microsoft.CodeAnalysis;

namespace Parquet.SourceGenerator.Diagnostics;

/// <summary>
/// Defines Roslyn diagnostic rules for compile-time validation of [ParquetSerializable] types.
/// </summary>
public static class DiagnosticDescriptors
{
    /// <summary>
    /// PARQ001: Target type decorated with [ParquetSerializable] must be partial.
    /// </summary>
    public static readonly DiagnosticDescriptor MustBePartial = new(
        id: "PARQ001",
        title: "Type decorated with [ParquetSerializable] must be partial",
        messageFormat: "The type '{0}' is decorated with [ParquetSerializable] but is not declared as partial",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// PARQ002: Duplicate Parquet column name detected.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateColumnName = new(
        id: "PARQ002",
        title: "Duplicate Parquet column name detected",
        messageFormat: "The Parquet column name '{0}' is specified multiple times on type '{1}'",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// PARQ003: No public serializable properties found.
    /// </summary>
    public static readonly DiagnosticDescriptor NoPropertiesFound = new(
        id: "PARQ003",
        title: "No public serializable properties found",
        messageFormat: "The type '{0}' is decorated with [ParquetSerializable] but has no public serializable properties or fields",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// PARQ004: Non-public property decorated with [ParquetColumn] will be ignored.
    /// </summary>
    public static readonly DiagnosticDescriptor NonPublicPropertyIgnored = new(
        id: "PARQ004",
        title: "Non-public property ignored",
        messageFormat: "The property '{0}' on type '{1}' is decorated with [ParquetColumn] but is not public and will be ignored",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// PARQ005: Invalid ParquetDecimal precision or scale configuration.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidDecimalPrecisionScale = new(
        id: "PARQ005",
        title: "Invalid ParquetDecimal precision or scale",
        messageFormat: "Invalid ParquetDecimal on property '{0}': precision ({1}) must be >= scale ({2}) and <= 38",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
