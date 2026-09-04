using System;

namespace Parquet.SourceGenerator.Models;

/// <summary>
/// Value-equatable model representing a class, record, or struct marked for Parquet code generation.
/// </summary>
/// <remarks>
/// Kept to exactly what the emitter reads. <c>SchemaName</c>, <c>IsRecord</c> and <c>IsValueType</c>
/// were all carried here and never used — dead state that still participated in the incremental
/// pipeline's equality comparisons, so a change to any of them would have invalidated the cache and
/// re-run generation for no output difference.
/// </remarks>
public sealed record TargetClassModel(
    string Namespace,
    string ClassName,
    EquatableArray<PropertyModel> Properties
) : IEquatable<TargetClassModel>;
