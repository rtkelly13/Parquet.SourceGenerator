using System;

namespace Parquet.SourceGenerator;

/// <summary>
/// Controls column binding parameters such as field name override and ordering.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ParquetColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="ParquetColumnAttribute"/> without renaming the column.
    /// </summary>
    /// <remarks>
    /// Exists so ordering can be expressed on its own — <c>[ParquetColumn(Order = 2)]</c>. With only
    /// the name-taking constructor available, reordering a column forced you to restate its name.
    /// </remarks>
    public ParquetColumnAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ParquetColumnAttribute"/> with the specified column name.
    /// </summary>
    /// <param name="name">The name of the column in the Parquet schema.</param>
    public ParquetColumnAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets or sets the name of the column in the Parquet schema. When null, the member name is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the explicit column index order.
    /// </summary>
    public int Order { get; set; } = -1;
}
