using System;

namespace Parquet.SourceGenerator;

/// <summary>
/// Specifies timestamp unit resolution for DateTime fields.
/// </summary>
public enum ParquetTimestampUnit
{
    /// <summary>
    /// Milliseconds since Unix epoch.
    /// </summary>
    Milliseconds,

    /// <summary>
    /// Microseconds since Unix epoch.
    /// </summary>
    Microseconds,

    /// <summary>
    /// Nanoseconds since Unix epoch.
    /// </summary>
    Nanoseconds
}

/// <summary>
/// Configures timestamp resolution for DateTime properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ParquetTimestampAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="ParquetTimestampAttribute"/>.
    /// </summary>
    /// <param name="unit">The timestamp unit resolution.</param>
    public ParquetTimestampAttribute(ParquetTimestampUnit unit)
    {
        Unit = unit;
    }

    /// <summary>
    /// Gets the timestamp resolution unit.
    /// </summary>
    public ParquetTimestampUnit Unit { get; }
}
