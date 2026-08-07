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

    /// <summary>
    /// PARQ006: Property or field type has no Parquet column representation.
    /// </summary>
    /// <remarks>
    /// The allowed set mirrors <c>Parquet.Encodings.SchemaEncoder.SupportedTypes</c>, so this only
    /// rejects what Parquet.Net itself rejects. Without it, an unsupported type produced a schema
    /// the library refused at runtime — a stack trace from inside Parquet.Net, a long way from the
    /// property that caused it.
    /// </remarks>
    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        id: "PARQ006",
        title: "Unsupported Parquet property type",
        messageFormat: "The member '{0}' on type '{2}' has type '{1}', which has no Parquet column representation. Remove it, mark it [ParquetIgnore], or change its type",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// PARQ007: Member cannot be assigned by the generated deserializer.
    /// </summary>
    /// <remarks>
    /// The read path materialises through an object initializer, so every column needs a setter (or
    /// <c>init</c>) reachable from the generated extension class. A get-only property or readonly
    /// field previously produced CS0200/CS0191 inside generated code, with nothing pointing back at
    /// the declaration responsible.
    /// </remarks>
    public static readonly DiagnosticDescriptor MemberNotAssignable = new(
        id: "PARQ007",
        title: "Parquet member is not assignable",
        messageFormat: "The member '{0}' on type '{1}' cannot be assigned by the generated deserializer. Give it an accessible set or init accessor, or mark it [ParquetIgnore]",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// PARQ008: Reference type has no accessible parameterless constructor.
    /// </summary>
    /// <remarks>
    /// Covers positional records (<c>record Person(int Id, string Name)</c>) and any class whose
    /// only constructors take arguments. The generated reader uses an object initializer, which
    /// needs one; without this the emitted code failed with CS7036.
    /// </remarks>
    public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        id: "PARQ008",
        title: "Parquet type has no accessible parameterless constructor",
        messageFormat: "The type '{0}' has no accessible parameterless constructor, so the generated deserializer cannot construct it. Add one, or declare the columns as settable members instead of primary constructor parameters",
        category: "ParquetSourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
