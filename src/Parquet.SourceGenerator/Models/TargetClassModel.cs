using System;

namespace Parquet.SourceGenerator.Models;

/// <summary>
/// Value-equatable model representing a class, record, or struct marked for Parquet code generation.
/// Memory layout ordered: reference pointers first, booleans at tail to minimize memory footprint in Roslyn incremental state caching.
/// </summary>
public sealed record TargetClassModel(
    string Namespace,
    string ClassName,
    string SchemaName,
    EquatableArray<PropertyModel> Properties,
    bool IsRecord,
    bool IsValueType) : IEquatable<TargetClassModel>;
