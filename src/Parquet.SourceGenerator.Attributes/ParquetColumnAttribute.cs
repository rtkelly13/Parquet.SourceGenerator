using System;

namespace Parquet.SourceGenerator;

/// <summary>
/// Controls column binding parameters such as field name override and ordering.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ParquetColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="ParquetColumnAttribute"/> with the specified column name.
    /// </summary>
    /// <param name="name">The name of the column in the Parquet schema.</param>
    public ParquetColumnAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the name of the column in the Parquet schema.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the explicit column index order.
    /// </summary>
    public int Order { get; set; } = -1;
}
