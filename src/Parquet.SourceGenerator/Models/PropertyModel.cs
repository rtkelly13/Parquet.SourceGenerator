using System;
using System.Diagnostics.CodeAnalysis;

namespace Parquet.SourceGenerator.Models;

/// <summary>
/// Classifies a property's Parquet field kind at parse time, driving fast switch-based code emission
/// rather than repeated TypeName string comparisons. Inspired by System.Text.Json's ConverterStrategy.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Enum members represent semantic property primitive types."
)]
public enum PropertyKind
{
    /// <summary>int, long, double, float, bool, string — direct DataField passthrough.</summary>
    Primitive,

    /// <summary>decimal → DecimalDataField with optional precision/scale attributes.</summary>
    Decimal,

    /// <summary>DateTime / DateTimeOffset → DateTimeDataField.</summary>
    DateTime,

    /// <summary>TimeSpan → TimeSpanDataField.</summary>
    TimeSpan,

    /// <summary>Guid → DataField&lt;string&gt; with .ToString() / Guid.Parse() interchange.</summary>
    Guid,

    /// <summary>enum types → DataField&lt;int&gt; (underlying type) with cast interchange.</summary>
    Enum,

    /// <summary>byte[] → DataField&lt;byte[]&gt; passthrough.</summary>
    ByteArray,
}

/// <summary>
/// Value-equatable model representing a single property or field binding.
/// Optimized memory layout: 8-byte reference pointers first, followed by 4-byte primitives, booleans at tail.
/// </summary>
public sealed record PropertyModel(
    string Name,
    string ParquetColumnName,
    string TypeName,
    string? TimestampUnit,
    string? EnumUnderlyingTypeName,
    int Order,
    int? DecimalPrecision,
    int? DecimalScale,
    PropertyKind Kind,
    bool IsNullable
) : IEquatable<PropertyModel>;
