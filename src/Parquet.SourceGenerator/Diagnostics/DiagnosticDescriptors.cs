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
}
