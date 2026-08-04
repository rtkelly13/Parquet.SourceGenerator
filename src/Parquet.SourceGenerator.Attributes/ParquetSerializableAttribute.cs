using System;

namespace Parquet.SourceGenerator;

/// <summary>
/// Triggers compile-time Parquet schema discovery, column serializer, and deserializer generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class ParquetSerializableAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an optional override for the Parquet schema name.
    /// </summary>
    public string? SchemaName { get; set; }
}
